using System;

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
    [Varve.HotPath]
    public static bool TryValidate(ReadOnlySpan<byte> utf8, out IriComponents components, out IriError error) =>
        IriScanner.TryValidate(utf8, out components, out error);

    /// <summary>
    /// Whether the input is a well-formed IRI <em>with a scheme</em>. RDF terms
    /// require one; a relative reference is not an RDF IRI until it is resolved.
    /// </summary>
    [Varve.HotPath]
    public static bool IsAbsolute(ReadOnlySpan<byte> utf8) =>
        IriScanner.TryValidate(utf8, out IriComponents components, out _) && components.HasScheme;

    /// <summary>
    /// The exact byte length <see cref="TryResolve"/> would produce, or -1 if
    /// the base is not an absolute IRI or either input is malformed.
    /// </summary>
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
    public static bool TryResolve(
        ReadOnlySpan<byte> baseIri,
        ReadOnlySpan<byte> reference,
        Span<byte> destination,
        out int written) =>
        IriResolver.TryResolve(baseIri, reference, destination, out written);
}
