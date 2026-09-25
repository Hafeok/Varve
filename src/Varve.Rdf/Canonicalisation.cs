// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Varve.Rdf;

/// <summary>How <see cref="RdfCanonicaliser"/> runs (<c>rdf-canon.md</c> §2).</summary>
public sealed class CanonicalisationOptions
{
    /// <summary>The defaults: SHA-256, and a work limit of 1,000.</summary>
    public static CanonicalisationOptions Default { get; } = new();

    /// <summary>
    /// The hash algorithm: SHA-256 (the default), SHA-384 or SHA-512
    /// (RDFC-1.0 §3.1: "MUST support SHA-256 and SHA-384").
    /// </summary>
    public HashAlgorithmName HashAlgorithm
    {
        get;
        init
        {
            if (value != HashAlgorithmName.SHA256 && value != HashAlgorithmName.SHA384 && value != HashAlgorithmName.SHA512)
            {
                throw new ArgumentException("RDFC-1.0 here supports SHA-256, SHA-384 and SHA-512.", nameof(value));
            }

            field = value;
        }
    } = HashAlgorithmName.SHA256;

    /// <summary>
    /// The work limit, as a multiple of the blank nodes whose first-degree
    /// hash is not unique: the most calls to Hash N-Degree Quads, and
    /// permutations examined by them, one canonicalisation may make (RDFC-1.0
    /// §4.4.3: "implementations MUST defend against potential
    /// denial-of-service attacks"). Exceeding it throws
    /// <see cref="CanonicalisationLimitException"/>. The default, 1,000, is
    /// measured: the W3C suite's most demanding computable case needs 279,
    /// and its poison graph meets 1,000 in tens of milliseconds.
    /// </summary>
    public int WorkLimit
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 1_000;
}

/// <summary>A canonicalised dataset: its canonical N-Quads form and the identifiers issued.</summary>
public sealed class CanonicalDataset
{
    internal CanonicalDataset(ReadOnlyMemory<byte> nquads, IReadOnlyDictionary<string, string> issued)
    {
        NQuads = nquads;
        IssuedIdentifiers = issued;
    }

    /// <summary>The canonical N-Quads form, UTF-8: one quad per line, sorted, each line ending in LF (RDFC-1.0 §4.4.3, Appendix A).</summary>
    public ReadOnlyMemory<byte> NQuads { get; }

    /// <summary>Each input blank node's label to its canonical identifier, <c>c14n0</c> onward (§4.2).</summary>
    public IReadOnlyDictionary<string, string> IssuedIdentifiers { get; }
}

/// <summary>
/// Canonicalisation stopped at its work limit (<see cref="CanonicalisationOptions.WorkLimit"/>):
/// the dataset is one whose blank nodes are too symmetric to canonicalise
/// within the bound — the "poison graph" of RDFC-1.0 §7.1.
/// </summary>
public sealed class CanonicalisationLimitException : Exception
{
    /// <summary>A limit exception with no detail.</summary>
    public CanonicalisationLimitException()
    {
    }

    /// <summary>A limit exception with a message.</summary>
    public CanonicalisationLimitException(string message)
        : base(message)
    {
    }

    /// <summary>A limit exception with a message and a cause.</summary>
    public CanonicalisationLimitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The steps taken when the limit was met, and the limit.</summary>
    public CanonicalisationLimitException(long steps, long limit)
        : base("Canonicalisation stopped after " + steps + " steps of Hash N-Degree Quads, its limit (RDFC-1.0 §4.4.3, §7.1).")
    {
        Steps = steps;
        Limit = limit;
    }

    /// <summary>The steps taken: calls to Hash N-Degree Quads and permutations examined.</summary>
    public long Steps { get; }

    /// <summary>The limit that was met.</summary>
    public long Limit { get; }
}
