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
using Varve.Store.Log;
using Varve.Store.S3.Model;

namespace Varve.Store.S3;

/// <summary>
/// The five object-store operations over the S3 REST API (ADR 0134):
/// PutObject with <c>If-None-Match: *</c> and <c>If-Match</c>, GetObject with
/// <c>Range</c>, ListObjectsV2 by prefix, DeleteObject, every request signed
/// with SigV4.
/// </summary>
/// <remarks>
/// <para>
/// **Credentials are the workload's web identity** (ADR 0137): temporary
/// credentials from <c>AssumeRoleWithWebIdentity</c>. A static development key
/// is accepted for a loopback endpoint only.
/// </para>
/// <para>
/// A condition that fails is <c>412</c>, and an <c>If-Match</c> on no object
/// <c>404</c>: both the contract's lost race. <c>409
/// ConditionalRequestConflict</c> is retried, as S3 asks; so is throttling. A
/// write is never retried after a <c>500</c> or a dropped connection, because
/// its outcome is then unknown and the backend finds out by reading
/// (ADR 0135).
/// </para>
/// </remarks>
[Contract(typeof(TheObjectStoreContract.FiveOperations), Role = "the object-store contract over the S3 REST API")]
public sealed class S3ObjectStore : IObjectStore, IDisposable
{
    private const int Attempts = 6;

    private readonly HttpClient _http;
    private readonly ICredentialSource _credentials;
    private readonly TimeProvider _clock;
    private readonly string _region;
    private readonly string _bucketAddress;

    private S3ObjectStore(S3ObjectStoreOptions options, ICredentialSource credentials, HttpClient http)
    {
        _clock = options.Clock;
        _credentials = credentials;
        _http = http;
        _region = options.Region.Value;
        string endpoint = options.Endpoint.GetLeftPart(UriPartial.Authority);

        // Amazon S3 by virtual host; any other server by path.
        _bucketAddress = options.Endpoint.Host.EndsWith(".amazonaws.com", StringComparison.OrdinalIgnoreCase)
            ? options.Endpoint.Scheme + "://" + options.Bucket.Value + "." + options.Endpoint.Authority
            : endpoint + "/" + options.Bucket.Value;
    }

    /// <summary>
    /// The bucket, signed with credentials from the workload's web identity,
    /// or with <see cref="S3ObjectStoreOptions.DevelopmentKey"/> when the
    /// endpoint is a loopback address.
    /// </summary>
    /// <exception cref="ArgumentException">A development key for a non-loopback endpoint, or no credentials configured.</exception>
    public static S3ObjectStore Create(S3ObjectStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Endpoint);
        ArgumentNullException.ThrowIfNull(options.Clock);
        Validate(options);

        HttpClient http = new(options.Handler ?? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }, disposeHandler: options.Handler is null)
        {
            Timeout = TimeSpan.FromMinutes(5),
        };

        ICredentialSource credentials;

        if (options.DevelopmentKey is { } key)
        {
            credentials = new StaticKey(key.AccessKeyId, key.SecretAccessKey);
        }
        else
        {
            Uri sts = options.StsEndpoint ?? new Uri("https://sts.amazonaws.com/");
            credentials = WebIdentity.FromEnvironment(Environment.GetEnvironmentVariable, http, options.Clock, sts)
                ?? throw new ArgumentException("No credentials for the S3 store: set AWS_WEB_IDENTITY_TOKEN_FILE and AWS_ROLE_ARN for web identity, the one source ADR 0137 admits beyond a loopback development key.", nameof(options));
        }

        return new S3ObjectStore(options, credentials, http);
    }

    /// <inheritdoc />
    public async ValueTask<ObjectVersion?> CreateIfAbsentAsync(ObjectName name, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Put, Address(name), content, ("If-None-Match", "*"), write: true, cancellationToken).ConfigureAwait(false);

        return response.StatusCode switch
        {
            HttpStatusCode.OK or HttpStatusCode.Created => Version(response),
            HttpStatusCode.PreconditionFailed => null,
            _ => throw Failure(response, "PUT", name),
        };
    }

    /// <inheritdoc />
    public async ValueTask<ObjectVersion?> ReplaceIfUnchangedAsync(ObjectName name, ObjectVersion expected, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Put, Address(name), content, ("If-Match", expected.Value), write: true, cancellationToken).ConfigureAwait(false);

        return response.StatusCode switch
        {
            HttpStatusCode.OK or HttpStatusCode.Created => Version(response),
            HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound => null,
            _ => throw Failure(response, "PUT", name),
        };
    }

    /// <inheritdoc />
    public async ValueTask<ObjectRead?> ReadAsync(ObjectName name, ByteOffset offset, ByteCount length, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset.Value, nameof(offset));
        ArgumentOutOfRangeException.ThrowIfNegative(length.Value, nameof(length));

        if (length.Value == 0)
        {
            return await HeadAsync(name, cancellationToken).ConfigureAwait(false);
        }

        bool whole = offset.Value == 0 && length.Value == long.MaxValue;
        (string, string)? range = whole
            ? null
            : ("Range", "bytes=" + offset.Value.ToString(CultureInfo.InvariantCulture) + "-" + (offset.Value + Math.Min(length.Value, long.MaxValue - offset.Value) - 1).ToString(CultureInfo.InvariantCulture));
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, Address(name), ReadOnlyMemory<byte>.Empty, range, write: false, cancellationToken).ConfigureAwait(false);

        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return null;

            case HttpStatusCode.RequestedRangeNotSatisfiable:
                return await HeadAsync(name, cancellationToken).ConfigureAwait(false);

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
        string? continuation = null;

        do
        {
            string query = "list-type=2&max-keys=1000&prefix=" + SigV4.Encode(prefix.Value, keepSlash: false)
                + (continuation is null ? string.Empty : "&continuation-token=" + SigV4.Encode(continuation, keepSlash: false));
            using HttpResponseMessage response = await SendAsync(HttpMethod.Get, new Uri(_bucketAddress + "?" + query), ReadOnlyMemory<byte>.Empty, null, write: false, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw Failure(response, "LIST", prefix);
            }

            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            continuation = ReadListing(body, entries);
        }
        while (continuation is not null);

        entries.Sort(static (left, right) => left.Name.CompareTo(right.Name));
        return entries;
    }

    /// <inheritdoc />
    /// <remarks>S3 answers a delete of no object as it answers any delete, so whether there was one is asked first.</remarks>
    public async ValueTask<bool> DeleteAsync(ObjectName name, CancellationToken cancellationToken)
    {
        if (await HeadAsync(name, cancellationToken).ConfigureAwait(false) is null)
        {
            return false;
        }

        using HttpResponseMessage response = await SendAsync(HttpMethod.Delete, Address(name), ReadOnlyMemory<byte>.Empty, null, write: false, cancellationToken).ConfigureAwait(false);

        return response.StatusCode switch
        {
            HttpStatusCode.NoContent or HttpStatusCode.OK => true,
            HttpStatusCode.NotFound => false,
            _ => throw Failure(response, "DELETE", name),
        };
    }

    /// <summary>The bucket's address: how a message names this store.</summary>
    public override string ToString() => _bucketAddress;

    /// <summary>Releases the connection pool.</summary>
    public void Dispose()
    {
        (_credentials as IDisposable)?.Dispose();
        _http.Dispose();
    }

    private static void Validate(S3ObjectStoreOptions options)
    {
        Uri endpoint = options.Endpoint;

        if (!endpoint.IsAbsoluteUri || (endpoint.Scheme != Uri.UriSchemeHttps && endpoint.Scheme != Uri.UriSchemeHttp) || !string.IsNullOrEmpty(endpoint.Query))
        {
            throw new ArgumentException("The endpoint '" + endpoint.GetLeftPart(UriPartial.Authority) + "' is not an absolute http or https address without a query.", nameof(options));
        }

        if (options.DevelopmentKey is not null && !endpoint.IsLoopback)
        {
            throw new ArgumentException("A static access key is accepted only for a loopback endpoint; '" + endpoint.GetLeftPart(UriPartial.Authority) + "' is not one. Use the workload's web identity (ADR 0137, LoopbackDevelopmentKey).", nameof(options));
        }

        if (endpoint.Scheme == Uri.UriSchemeHttp && !endpoint.IsLoopback)
        {
            throw new ArgumentException("The endpoint '" + endpoint.GetLeftPart(UriPartial.Authority) + "' is plain http on a non-loopback host.", nameof(options));
        }
    }

    private static ObjectVersion Version(HttpResponseMessage response) =>
        response.Headers.ETag is { } tag ? new ObjectVersion(tag.Tag) : throw new IOException("The S3 store answered without an ETag.");

    private static IOException Failure(HttpResponseMessage response, string operation, ObjectName name)
    {
        string hint = response.StatusCode is HttpStatusCode.Forbidden ? " The role needs s3:GetObject, s3:PutObject, s3:DeleteObject and s3:ListBucket on the bucket (ADR 0137)." : string.Empty;
        return new IOException("The S3 store answered " + (int)response.StatusCode + " to " + operation + " '" + name + "'." + hint);
    }

    private static string? ReadListing(Stream body, List<ObjectEntry> entries)
    {
        using XmlReader xml = XmlReader.Create(body, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        string? continuation = null;
        bool truncated = false;
        string? key = null;
        long size = 0;
        string? tag = null;
        bool advance = true;

        while (!advance || xml.Read())
        {
            advance = true;

            if (xml.NodeType == XmlNodeType.EndElement && xml.LocalName == "Contents" && key is not null)
            {
                string version = tag is null ? "\"\"" : tag.StartsWith('"') ? tag : "\"" + tag + "\"";
                entries.Add(new ObjectEntry(new ObjectName(key), new ByteCount(size), new ObjectVersion(version)));
                key = null;
                tag = null;
                size = 0;
                continue;
            }

            if (xml.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            switch (xml.LocalName)
            {
                case "Key":
                    key = xml.ReadElementContentAsString();
                    advance = false;
                    break;
                case "Size":
                    size = long.Parse(xml.ReadElementContentAsString(), NumberStyles.None, CultureInfo.InvariantCulture);
                    advance = false;
                    break;
                case "ETag":
                    tag = xml.ReadElementContentAsString();
                    advance = false;
                    break;
                case "IsTruncated":
                    truncated = xml.ReadElementContentAsString() == "true";
                    advance = false;
                    break;
                case "NextContinuationToken":
                    continuation = xml.ReadElementContentAsString();
                    advance = false;
                    break;
            }
        }

        return truncated ? continuation : null;
    }

    private Uri Address(ObjectName name) => new(_bucketAddress + "/" + SigV4.Encode(name.Value, keepSlash: true));

    private async ValueTask<ObjectRead?> HeadAsync(ObjectName name, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Head, Address(name), ReadOnlyMemory<byte>.Empty, null, write: false, cancellationToken).ConfigureAwait(false);

        return response.StatusCode switch
        {
            HttpStatusCode.OK => new ObjectRead(ReadOnlyMemory<byte>.Empty, Version(response), new ByteCount(response.Content.Headers.ContentLength ?? 0)),
            HttpStatusCode.NotFound => null,
            _ => throw Failure(response, "HEAD", name),
        };
    }

    // Sends, signing each attempt afresh. Throttling and S3's 409
    // ConditionalRequestConflict are retried for every operation; a server
    // error or a dropped connection only for one that is safe to repeat.
    private async ValueTask<HttpResponseMessage> SendAsync(HttpMethod method, Uri address, ReadOnlyMemory<byte> content, (string Name, string Value)? header, bool write, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            using HttpRequestMessage request = new(method, address);

            if (method == HttpMethod.Put)
            {
                request.Content = new ReadOnlyMemoryContent(content);
            }

            if (header is { } extra)
            {
                request.Headers.TryAddWithoutValidation(extra.Name, extra.Value);
            }

            SigningCredentials credentials = await _credentials.GetAsync(cancellationToken).ConfigureAwait(false);
            SigV4.Sign(request, content.Span, credentials, _region, _clock.GetUtcNow());
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

            bool again = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable
                || (write && response.StatusCode == HttpStatusCode.Conflict);
            bool failed = response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout;

            if (attempt < Attempts && (again || (failed && !write)))
            {
                TimeSpan wait = response.Headers.RetryAfter?.Delta is { } delta && delta < TimeSpan.FromSeconds(30) ? delta : Backoff(attempt);
                response.Dispose();
                await Task.Delay(wait, _clock, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (failed && write)
            {
                response.Dispose();
                throw new IOException("The S3 store answered " + (int)response.StatusCode + " to a write; whether it was applied is unknown.");
            }

            return response;
        }
    }

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromMilliseconds(100 << Math.Min(attempt - 1, 6));
}
