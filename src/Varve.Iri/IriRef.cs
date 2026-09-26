// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Iri;

/// <summary>
/// Validation and resolution of IRI references, over UTF-8.
/// </summary>
/// <remarks>
/// <para>
/// RFC 3987 §2.2 for the syntax, RFC 3986 §5 for reference resolution. See
/// <c>docs/spec/iri.md</c>.
/// </para>
/// <para>
/// **Nothing here normalises.** RDF IRI equality is byte equality (RDF 1.1
/// Concepts §3.2), so scheme and host case, percent-encoding and Unicode
/// normalisation are all left exactly as the input had them. Resolution
/// rewrites by construction and is the only operation that changes bytes.
/// </para>
/// </remarks>
public static class IriRef
{
    /// <summary>
    /// Validates an IRI reference and reports its components as byte ranges
    /// into <paramref name="utf8"/>.
    /// </summary>
    /// <param name="utf8">The reference, as UTF-8. Not copied.</param>
    /// <param name="components">The components, when this returns true.</param>
    /// <param name="error">The failure, when this returns false.</param>
    /// <returns>True if the input is a well-formed <c>IRI-reference</c>.</returns>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public static bool TryValidate(ReadOnlySpan<byte> utf8, out IriComponents components, out IriError error) =>
        IriScanner.TryValidate(utf8, out components, out error);

    /// <summary>
    /// Whether the input is a well-formed IRI <em>with a scheme</em>. RDF terms
    /// require one; a relative reference is not an RDF IRI until it is resolved.
    /// </summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public static bool IsAbsolute(ReadOnlySpan<byte> utf8) =>
        IriScanner.TryValidate(utf8, out IriComponents components, out _) && components.HasScheme;

    /// <summary>
    /// Whether the input begins with a <c>scheme</c> and a colon (RFC 3986
    /// §3.1), which is what decides whether it needs resolving against a base.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="IsAbsolute"/>, which also requires the whole
    /// reference to be well-formed. A caller that has chosen not to validate
    /// still has to decide whether to resolve, and using validity for that
    /// makes a malformed absolute IRI look relative — so the two questions are
    /// answered separately.
    /// </remarks>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public static bool StartsWithScheme(ReadOnlySpan<byte> utf8)
    {
        if (utf8.IsEmpty || !IsSchemeStart(utf8[0]))
        {
            return false;
        }

        for (int i = 1; i < utf8.Length; i++)
        {
            byte b = utf8[i];

            if (b == (byte)':')
            {
                return true;
            }

            if (!IsSchemeStart(b) && b is not ((>= (byte)'0' and <= (byte)'9')
                or (byte)'+' or (byte)'-' or (byte)'.'))
            {
                return false;
            }
        }

        return false;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private static bool IsSchemeStart(byte b) =>
        b is (>= (byte)'a' and <= (byte)'z') or (>= (byte)'A' and <= (byte)'Z');

    /// <summary>
    /// The exact byte length <see cref="TryResolve"/> would produce, or -1 if
    /// the base is not an absolute IRI or either input is malformed.
    /// </summary>
    [DesignDecision(typeof(SpanBoundaryCounts.SpanWriterCountsAreInt), Scope = ExceptionScope.Boundary)]
    public static int ResolveLength(ReadOnlySpan<byte> baseIri, ReadOnlySpan<byte> reference) =>
        IriResolver.TryResolve(baseIri, reference, default, out int written) || written > 0
            ? written
            : -1;

    /// <summary>
    /// Resolves <paramref name="reference"/> against <paramref name="baseIri"/>
    /// per RFC 3986 §5, strictly.
    /// </summary>
    /// <param name="baseIri">An absolute IRI.</param>
    /// <param name="reference">Any IRI reference.</param>
    /// <param name="destination">Where to write the result.</param>
    /// <param name="written">
    /// The bytes written on success; on failure for lack of space, the length
    /// required. Zero when the inputs themselves were rejected.
    /// </param>
    /// <returns>
    /// True on success. False when the base is not absolute, when either input
    /// is malformed, or when <paramref name="destination"/> is too small — the
    /// last of which is told apart by <paramref name="written"/> being non-zero.
    /// </returns>
    [DesignDecision(typeof(SpanBoundaryCounts.SpanWriterCountsAreInt), Scope = ExceptionScope.Boundary)]
    public static bool TryResolve(
        ReadOnlySpan<byte> baseIri,
        ReadOnlySpan<byte> reference,
        Span<byte> destination,
        out int written) =>
        IriResolver.TryResolve(baseIri, reference, destination, out written);
}
