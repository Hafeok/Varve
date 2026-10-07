// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Claims;
using System.Text;
using Varve.Iri;
using Varve.Protocol;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Server;

/// <summary>
/// The commit agent from the validated token (ADRs 0037, 0094): the issuer,
/// <c>#</c>, and the subject — <c>oid</c> for Entra, <c>sub</c> otherwise, as
/// configured — percent-encoded to RFC 3986's unreserved characters, so that
/// any subject makes an IRI and two subjects never make one.
/// </summary>
internal sealed class TokenIdentity(string subjectClaim) : ICallerIdentity
{
    public RequestTerm AgentOf(ClaimsPrincipal caller)
    {
        string? issuer = caller.FindFirst("iss")?.Value;
        string? subject = caller.FindFirst(subjectClaim)?.Value;

        if (string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(subject) || issuer.Contains('#'))
        {
            return RequestTerm.None;
        }

        byte[] iri = Encoding.UTF8.GetBytes(issuer + "#" + Encode(subject));
        return IriRef.TryValidate(iri, out IriComponents components, out _) && components.HasScheme
            ? RdfTerm.Iri(iri)
            : RequestTerm.None;
    }

    internal static string Encode(string subject)
    {
        StringBuilder encoded = new(subject.Length * 3);

        foreach (byte b in Encoding.UTF8.GetBytes(subject))
        {
            bool unreserved = b is (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'a' and <= (byte)'z') or (>= (byte)'0' and <= (byte)'9')
                or (byte)'-' or (byte)'.' or (byte)'_' or (byte)'~';

            if (unreserved)
            {
                encoded.Append((char)b);
            }
            else
            {
                encoded.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return encoded.ToString();
    }
}

/// <summary>Anonymous mode: no one is authenticated, so no commit names an agent (ADR 0094).</summary>
internal sealed class NoAgent : ICallerIdentity
{
    public RequestTerm AgentOf(ClaimsPrincipal caller) => RequestTerm.None;
}
