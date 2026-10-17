// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Azure.Model;
using Varve.Store.Log;

namespace Varve.Store.Azure;

/// <summary>
/// The five object-store operations over Azure Blob Storage's REST API
/// (ADR 0134): Put Blob with <c>If-None-Match: *</c> and <c>If-Match</c>, Get
/// Blob with <c>x-ms-range</c>, List Blobs by prefix, Delete Blob.
/// </summary>
/// <remarks>
/// <para>
/// **Authenticated by the workload's identity** (ADR 0137): the Container
/// Apps managed-identity endpoint, a workload-identity token file, or on a
/// workstation <c>az account get-access-token</c>. Azurite on a loopback
/// address is signed with its published development key instead, and nothing
/// else ever is.
/// </para>
/// <para>
/// A create that finds a blob is answered <c>409 BlobAlreadyExists</c>, a
/// replace whose version no longer matches <c>412 ConditionNotMet</c>; both
/// are the contract's lost race. Throttling (<c>429</c>, <c>503</c>) is retried
/// as <c>Retry-After</c> says; a read, a list or a delete is also retried after
/// a <c>500</c> or a dropped connection, but a write is not, because its
/// outcome is then unknown and the backend finds it out by reading (ADR 0135).
/// </para>
/// </remarks>
[Contract(typeof(TheObjectStoreContract.FiveOperations), Role = "the object-store contract over Azure Blob Storage")]
public sealed class AzureBlobStore : IObjectStore, IDisposable
{
    /// <summary>The service version every request names (ADR 0134, AzureServiceVersionPinned).</summary>
    private const string ServiceVersion = "2025-11-05";

    private const int Attempts = 6;

    private readonly HttpClient _http;
    private readonly IRequestSigner _signer;
    private readonly TimeProvider _clock;
    private readonly string _container;

    internal AzureBlobStore(AzureBlobStoreOptions options, IRequestSigner signer, HttpClient http)
    {
        _clock = options.Clock;
        _signer = signer;
        _http = http;
        _container = options.ContainerUrl.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    /// <summary>
    /// The container at <see cref="AzureBlobStoreOptions.ContainerUrl"/>,
    /// authenticated by the workload's identity, or by Azurite's development
    /// key when the address is a loopback address and the account Azurite's.
    /// </summary>
    /// <exception cref="ArgumentException">The address carries a query, such as a SAS token, or is not http(s).</exception>
    public static AzureBlobStore Create(AzureBlobStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.ContainerUrl);
        ArgumentNullException.ThrowIfNull(options.Clock);
        Validate(options.ContainerUrl);

        bool owns = options.Handler is null;
        HttpClient http = new(options.Handler ?? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }, disposeHandler: owns)
        {
            Timeout = TimeSpan.FromMinutes(5),
        };

        IRequestSigner signer = IsDevelopmentStorage(options.ContainerUrl)
            ? new DevelopmentKeySigner(options.Clock)
            : new BearerSigner(new CachedToken(TokenSources.FromEnvironment(Environment.GetEnvironmentVariable, http, options.Clock), options.Clock));
        return new AzureBlobStore(options, signer, http);
    }

    /// <summary>Whether the address is Azurite's on a loopback address, which the development key may sign.</summary>
    internal static bool IsDevelopmentStorage(Uri container) =>
        container.IsLoopback && container.AbsolutePath.TrimStart('/').StartsWith(DevelopmentKeySigner.Account + "/", StringComparison.Ordinal);

    /// <inheritdoc />
    public async ValueTask<ObjectVersion?> CreateIfAbsentAsync(ObjectName name, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(() => Put(name, content, "If-None-Match", "*"), write: true, cancellationToken).ConfigureAwait(false);

        return response.StatusCode switch
        {
            HttpStatusCode.Created => Version(response),
            HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed => null,
            _ => throw Failure(response, "PUT", name),
        };
    }

    /// <inheritdoc />
    public async ValueTask<ObjectVersion?> ReplaceIfUnchangedAsync(ObjectName name, ObjectVersion expected, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(() => Put(name, content, "If-Match", expected.Value), write: true, cancellationToken).ConfigureAwait(false);

        return response.StatusCode switch
        {
            HttpStatusCode.Created => Version(response),
            HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound or HttpStatusCode.Conflict => null,
            _ => throw Failure(response, "PUT", name),
        };
    }

    /// <inheritdoc />
    public async ValueTask<ObjectRead?> ReadAsync(ObjectName name, ByteOffset offset, ByteCount length, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset.Value, nameof(offset));
        ArgumentOutOfRangeException.ThrowIfNegative(length.Value, nameof(length));
        bool whole = offset.Value == 0 && length.Value == long.MaxValue;

        if (length.Value == 0)
        {
            return await PropertiesAsync(name, cancellationToken).ConfigureAwait(false);
        }

        using HttpResponseMessage response = await SendAsync(
            () =>
            {
                HttpRequestMessage request = new(HttpMethod.Get, BlobAddress(name));

                if (!whole)
                {
                    long last = offset.Value + Math.Min(length.Value, long.MaxValue - offset.Value) - 1;
                    request.Headers.Add("x-ms-range", "bytes=" + offset.Value.ToString(CultureInfo.InvariantCulture) + "-" + last.ToString(CultureInfo.InvariantCulture));
                }

                return request;
            },
            write: false,
            cancellationToken).ConfigureAwait(false);

        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return null;

            case HttpStatusCode.RequestedRangeNotSatisfiable:
                // From the end or beyond, or an empty blob: no bytes, and its length.
                return await PropertiesAsync(name, cancellationToken).ConfigureAwait(false);

            case HttpStatusCode.OK or HttpStatusCode.PartialContent:
                byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                long total = response.Content.Headers.ContentRange?.Length ?? bytes.Length;
                return new ObjectRead(bytes, Version(response), new ByteCount(total));

            default:
                throw Failure(response, "GET", name);
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ObjectEntry>> ListAsync(ObjectName prefix, CancellationToken cancellationToken)
    {
        List<ObjectEntry> entries = [];
        string? marker = null;

        do
        {
            string query = "restype=container&comp=list&maxresults=5000&prefix=" + Uri.EscapeDataString(prefix.Value)
                + (marker is null ? string.Empty : "&marker=" + Uri.EscapeDataString(marker));
            using HttpResponseMessage response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, new Uri(_container + "?" + query)), write: false, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw Failure(response, "LIST", prefix);
            }

            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            marker = await ReadListingAsync(body, entries).ConfigureAwait(false);
        }
        while (!string.IsNullOrEmpty(marker));

        entries.Sort(static (left, right) => left.Name.CompareTo(right.Name));
        return entries;
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(ObjectName name, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, BlobAddress(name)), write: false, cancellationToken).ConfigureAwait(false);

        return response.StatusCode switch
        {
            HttpStatusCode.Accepted or HttpStatusCode.OK => true,
            HttpStatusCode.NotFound => false,
            _ => throw Failure(response, "DELETE", name),
        };
    }

    /// <summary>The container's address, without a query: how a message names this store.</summary>
    public override string ToString() => _container;

    /// <summary>Releases the connection pool, and the token source's.</summary>
    public void Dispose()
    {
        _signer.Dispose();
        _http.Dispose();
    }

    private static void Validate(Uri container)
    {
        if (!container.IsAbsoluteUri || (container.Scheme != Uri.UriSchemeHttps && container.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("The container address '" + container.GetLeftPart(UriPartial.Path) + "' is not an absolute http or https address.", nameof(container));
        }

        if (!string.IsNullOrEmpty(container.Query))
        {
            // Never echoed: a query here is likely a SAS token.
            throw new ArgumentException("The container address carries a query. A SAS token is a key, refused by ADR 0137: the workload's identity authenticates instead.", nameof(container));
        }

        if (container.Scheme == Uri.UriSchemeHttp && !container.IsLoopback)
        {
            throw new ArgumentException("The container address '" + container.GetLeftPart(UriPartial.Path) + "' is plain http on a non-loopback host; Azure Storage is reached over https.", nameof(container));
        }
    }

    private static ObjectVersion Version(HttpResponseMessage response) =>
        response.Headers.ETag is { } tag ? new ObjectVersion(tag.Tag) : throw new IOException("Azure Blob Storage answered without an ETag.");

    private static IOException Failure(HttpResponseMessage response, string operation, ObjectName name)
    {
        string code = response.Headers.TryGetValues("x-ms-error-code", out IEnumerable<string>? values) ? string.Join(",", values) : "no error code";
        string hint = response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
            ? " The workload's identity needs Storage Blob Data Contributor on the container (ADR 0137)."
            : string.Empty;
        return new IOException("Azure Blob Storage answered " + (int)response.StatusCode + " " + code + " to " + operation + " '" + name + "'." + hint);
    }

    private static async ValueTask<string?> ReadListingAsync(Stream body, List<ObjectEntry> entries)
    {
        using XmlReader xml = XmlReader.Create(body, new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        string? marker = null;
        string? name = null;
        long length = 0;
        string? tag = null;

        // Reading an element's content leaves the reader on the node after
        // it, so the next read only happens when nothing was consumed.
        bool advance = true;

        while (!advance || await xml.ReadAsync().ConfigureAwait(false))
        {
            advance = true;

            if (xml.NodeType == XmlNodeType.EndElement && xml.LocalName == "Blob" && name is not null && tag is not null)
            {
                entries.Add(new ObjectEntry(new ObjectName(name), new ByteCount(length), new ObjectVersion(tag.StartsWith('"') ? tag : "\"" + tag + "\"")));
                name = null;
                tag = null;
                length = 0;
                continue;
            }

            if (xml.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            switch (xml.LocalName)
            {
                case "Name":
                    name = await xml.ReadElementContentAsStringAsync().ConfigureAwait(false);
                    advance = false;
                    break;
                case "Content-Length":
                    length = long.Parse(await xml.ReadElementContentAsStringAsync().ConfigureAwait(false), NumberStyles.None, CultureInfo.InvariantCulture);
                    advance = false;
                    break;
                case "Etag":
                    tag = await xml.ReadElementContentAsStringAsync().ConfigureAwait(false);
                    advance = false;
                    break;
                case "NextMarker":
                    marker = await xml.ReadElementContentAsStringAsync().ConfigureAwait(false);
                    advance = false;
                    break;
            }
        }

        return marker;
    }

    private Uri BlobAddress(ObjectName name) => new(_container + "/" + name.Value);

    private HttpRequestMessage Put(ObjectName name, ReadOnlyMemory<byte> content, string condition, string value)
    {
        HttpRequestMessage request = new(HttpMethod.Put, BlobAddress(name)) { Content = new ReadOnlyMemoryContent(content) };
        request.Headers.Add("x-ms-blob-type", "BlockBlob");
        request.Headers.TryAddWithoutValidation(condition, value);
        return request;
    }

    private async ValueTask<ObjectRead?> PropertiesAsync(ObjectName name, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Head, BlobAddress(name)), write: false, cancellationToken).ConfigureAwait(false);

        return response.StatusCode switch
        {
            HttpStatusCode.OK => new ObjectRead(ReadOnlyMemory<byte>.Empty, Version(response), new ByteCount(response.Content.Headers.ContentLength ?? 0)),
            HttpStatusCode.NotFound => null,
            _ => throw Failure(response, "HEAD", name),
        };
    }

    // Sends, signing each attempt afresh. Throttling is retried for every
    // operation; a server error or a dropped connection only for one whose
    // repetition is harmless, because a write's outcome is then unknown.
    private async ValueTask<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> build, bool write, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            using HttpRequestMessage request = build();
            request.Headers.Add("x-ms-version", ServiceVersion);
            await _signer.SignAsync(request, cancellationToken).ConfigureAwait(false);
            HttpResponseMessage response;

            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (!write && attempt < Attempts && error is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(Backoff(attempt), _clock, cancellationToken).ConfigureAwait(false);
                continue;
            }

            bool throttled = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;
            bool failed = response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout;

            if (attempt < Attempts && (throttled || (failed && !write)))
            {
                TimeSpan wait = RetryAfter(response) ?? Backoff(attempt);
                response.Dispose();
                await Task.Delay(wait, _clock, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (failed && write)
            {
                response.Dispose();
                throw new IOException("Azure Blob Storage answered " + (int)response.StatusCode + " to a write; whether it was applied is unknown.");
            }

            return response;
        }
    }

    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is not { } after)
        {
            return null;
        }

        TimeSpan wait = after.Delta ?? (after.Date is { } date ? date - _clock.GetUtcNow() : TimeSpan.Zero);
        return wait < TimeSpan.Zero ? TimeSpan.Zero : wait > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : wait;
    }

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromMilliseconds(100 << Math.Min(attempt - 1, 6));
}
