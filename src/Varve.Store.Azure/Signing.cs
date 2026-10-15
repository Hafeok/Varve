// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Store.Azure;

/// <summary>How a request to the store is authorised.</summary>
[Contract(typeof(StorageCredentialsAreWorkloadIdentity.OutboundWorkloadIdentity), Role = "how a request to Azure Storage is authorised")]
internal interface IRequestSigner : IDisposable
{
    ValueTask SignAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}

/// <summary>A bearer token from the workload's identity (ADR 0137).</summary>
internal sealed class BearerSigner(CachedToken token) : IRequestSigner
{
    public async ValueTask SignAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        AccessToken held = await token.GetAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", held.Value);
    }

    public void Dispose() => token.Dispose();
}

/// <summary>
/// Shared Key with Azurite's published development key, and only that, and
/// only for a loopback endpoint (ADR 0137, LoopbackDevelopmentKey). Microsoft
/// publishes the key; it "does not serve any security purpose".
/// </summary>
internal sealed class DevelopmentKeySigner(TimeProvider clock) : IRequestSigner
{
    public const string Account = "devstoreaccount1";

    private const string Key = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    public ValueTask SignAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is not { IsLoopback: true } address)
        {
            throw new InvalidOperationException("The development key signs requests to a loopback endpoint only (ADR 0137).");
        }

        request.Headers.Remove("x-ms-date");
        request.Headers.Add("x-ms-date", clock.GetUtcNow().ToString("r", CultureInfo.InvariantCulture));

        long length = request.Content?.Headers.ContentLength ?? 0;
        StringBuilder text = new();
        text.Append(request.Method.Method).Append('\n');
        text.Append('\n'); // Content-Encoding
        text.Append('\n'); // Content-Language
        text.Append(length == 0 ? string.Empty : length.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append('\n'); // Content-MD5
        text.Append(request.Content?.Headers.ContentType?.ToString() ?? string.Empty).Append('\n');
        text.Append('\n'); // Date
        text.Append('\n'); // If-Modified-Since
        text.Append(Header(request, "If-Match")).Append('\n');
        text.Append(Header(request, "If-None-Match")).Append('\n');
        text.Append('\n'); // If-Unmodified-Since
        text.Append('\n'); // Range

        foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers
            .Where(static h => h.Key.StartsWith("x-ms-", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static h => h.Key.ToLowerInvariant(), StringComparer.Ordinal))
        {
            text.Append(header.Key.ToLowerInvariant()).Append(':').Append(string.Join(",", header.Value)).Append('\n');
        }

        text.Append('/').Append(Account).Append(address.AbsolutePath);

        foreach ((string name, string value) in Query(address.Query).OrderBy(static q => q.Name, StringComparer.Ordinal))
        {
            text.Append('\n').Append(name).Append(':').Append(value);
        }

        string signature = Convert.ToBase64String(HMACSHA256.HashData(Convert.FromBase64String(Key), Encoding.UTF8.GetBytes(text.ToString())));
        request.Headers.TryAddWithoutValidation("Authorization", "SharedKey " + Account + ":" + signature);
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
    }

    private static string Header(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out IEnumerable<string>? values) ? string.Join(",", values) : string.Empty;

    private static IEnumerable<(string Name, string Value)> Query(string query)
    {
        foreach (string part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = part.IndexOf('=', StringComparison.Ordinal);
            string name = Uri.UnescapeDataString(equals < 0 ? part : part[..equals]).ToLowerInvariant();
            string value = equals < 0 ? string.Empty : Uri.UnescapeDataString(part[(equals + 1)..]);
            yield return (name, value);
        }
    }
}
