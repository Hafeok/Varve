// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace Varve.Store.S3;

/// <summary>
/// AWS Signature Version 4 for S3, with the payload hashed: a canonical
/// request, a string to sign, and a key derived from the secret per day,
/// region and service.
/// </summary>
internal static class SigV4
{
    private const string Algorithm = "AWS4-HMAC-SHA256";
    private const string Service = "s3";

    /// <summary>Adds the date, the payload hash, the session token and the signature to a request.</summary>
    public static void Sign(HttpRequestMessage request, ReadOnlySpan<byte> payload, SigningCredentials credentials, string region, DateTimeOffset now)
    {
        Uri address = request.RequestUri!;
        string date = now.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        string stamp = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        string payloadHash = Hex(SHA256.HashData(payload));

        request.Headers.Remove("x-amz-date");
        request.Headers.Remove("x-amz-content-sha256");
        request.Headers.TryAddWithoutValidation("x-amz-date", stamp);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);

        if (credentials.SessionToken is { } session)
        {
            request.Headers.Remove("x-amz-security-token");
            request.Headers.TryAddWithoutValidation("x-amz-security-token", session);
        }

        SortedDictionary<string, string> headers = new(StringComparer.Ordinal)
        {
            ["host"] = address.IsDefaultPort ? address.Host : address.Host + ":" + address.Port.ToString(CultureInfo.InvariantCulture),
        };

        foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
        {
            string name = header.Key.ToLowerInvariant();

            if (name.StartsWith("x-amz-", StringComparison.Ordinal) || name is "if-match" or "if-none-match" or "range")
            {
                headers[name] = string.Join(",", header.Value.Select(static v => v.Trim()));
            }
        }

        string signedHeaders = string.Join(";", headers.Keys);
        StringBuilder canonical = new();
        canonical.Append(request.Method.Method).Append('\n');
        canonical.Append(CanonicalPath(address.AbsolutePath)).Append('\n');
        canonical.Append(CanonicalQuery(address.Query)).Append('\n');

        foreach ((string name, string value) in headers)
        {
            canonical.Append(name).Append(':').Append(value).Append('\n');
        }

        canonical.Append('\n').Append(signedHeaders).Append('\n').Append(payloadHash);

        string scope = date + "/" + region + "/" + Service + "/aws4_request";
        string toSign = Algorithm + "\n" + stamp + "\n" + scope + "\n" + Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));

        byte[] key = HMACSHA256.HashData(Encoding.UTF8.GetBytes("AWS4" + credentials.SecretAccessKey), Encoding.UTF8.GetBytes(date));
        key = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(region));
        key = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(Service));
        key = HMACSHA256.HashData(key, "aws4_request"u8);
        string signature = Hex(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(toSign)));

        request.Headers.TryAddWithoutValidation("Authorization", Algorithm + " Credential=" + credentials.AccessKeyId + "/" + scope + ", SignedHeaders=" + signedHeaders + ", Signature=" + signature);
    }

    /// <summary>RFC 3986's unreserved characters as they are, everything else percent-encoded.</summary>
    public static string Encode(string value, bool keepSlash)
    {
        StringBuilder encoded = new(value.Length);

        foreach (byte b in Encoding.UTF8.GetBytes(value))
        {
            char c = (char)b;

            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~' || (keepSlash && c == '/'))
            {
                encoded.Append(c);
            }
            else
            {
                encoded.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return encoded.ToString();
    }

    private static string CanonicalPath(string path) => path.Length == 0 ? "/" : Encode(Uri.UnescapeDataString(path), keepSlash: true);

    private static string CanonicalQuery(string query)
    {
        List<(string Name, string Value)> parts = [];

        foreach (string part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = part.IndexOf('=', StringComparison.Ordinal);
            string name = Uri.UnescapeDataString(equals < 0 ? part : part[..equals]);
            string value = equals < 0 ? string.Empty : Uri.UnescapeDataString(part[(equals + 1)..]);
            parts.Add((Encode(name, keepSlash: false), Encode(value, keepSlash: false)));
        }

        parts.Sort(static (left, right) => left.Name != right.Name ? string.CompareOrdinal(left.Name, right.Name) : string.CompareOrdinal(left.Value, right.Value));
        return string.Join("&", parts.Select(static p => p.Name + "=" + p.Value));
    }

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);
}
