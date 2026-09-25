// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Sparql;

/// <summary>How to parse: the base IRI for relative references, and the widest version accepted.</summary>
/// <remarks>
/// The default value asks for SPARQL 1.2 with no base, so that
/// <c>default(SparqlParseOptions)</c> is the ordinary case. A <c>BASE</c>
/// declaration in the text takes precedence over <see cref="BaseIri"/>, and a
/// <c>VERSION</c> declaration may narrow <see cref="Version"/> and never widen
/// it (<c>docs/spec/sparql-grammar.md</c> §5).
/// </remarks>
public readonly struct SparqlParseOptions : IEquatable<SparqlParseOptions>
{
    /// <summary>Options with a base IRI and a version.</summary>
    public SparqlParseOptions(ReadOnlyMemory<byte> baseIri, SparqlVersion version = SparqlVersion.Sparql12)
    {
        BaseIri = baseIri;
        Version = version;
    }

    /// <summary>The base IRI relative references resolve against, as UTF-8. Empty for none.</summary>
    public ReadOnlyMemory<byte> BaseIri { get; init; }

    /// <summary>
    /// The widest version the caller accepts. The default value of the enum,
    /// which is what <c>default(SparqlParseOptions)</c> carries, means
    /// <see cref="SparqlVersion.Sparql12"/>.
    /// </summary>
    public SparqlVersion Version { get; init; }

    /// <summary><see cref="Version"/> with the default resolved.</summary>
    internal SparqlVersion EffectiveVersion => Version == 0 ? SparqlVersion.Sparql12 : Version;

    /// <inheritdoc />
    public bool Equals(SparqlParseOptions other) => EffectiveVersion == other.EffectiveVersion && BaseIri.Span.SequenceEqual(other.BaseIri.Span);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SparqlParseOptions other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(EffectiveVersion);
        hash.AddBytes(BaseIri.Span);
        return hash.ToHashCode();
    }

    /// <summary>Same version and base.</summary>
    public static bool operator ==(SparqlParseOptions left, SparqlParseOptions right) => left.Equals(right);

    /// <summary>Different version or base.</summary>
    public static bool operator !=(SparqlParseOptions left, SparqlParseOptions right) => !left.Equals(right);
}
