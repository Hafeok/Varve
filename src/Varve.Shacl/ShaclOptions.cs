// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Varve.Rdf;

namespace Varve.Shacl;

/// <summary>How a shapes graph is loaded and a validation runs (ADR 0155).</summary>
public sealed class ShaclOptions
{
    /// <summary>The defaults.</summary>
    public static ShaclOptions Default { get; } = new();

    /// <summary>SHACL 1.2 Core §6.8.1.2's default conformance-disallow set: <c>sh:Violation</c>, <c>sh:Warning</c>, <c>sh:Info</c>.</summary>
    public static ImmutableArray<RdfTerm> DefaultConformanceDisallows { get; } = [Vocabulary.Violation, Vocabulary.Warning, Vocabulary.Info];

    /// <summary>Constraints SHACL Core does not define, such as SHACL-SPARQL's (ADR 0148). None by default.</summary>
    public IReadOnlyList<IShaclExtension> Extensions { get; init; } = [];

    /// <summary>
    /// The longest one <c>sh:pattern</c> match may take; longer is a failure
    /// of the validation, never a hang (ADR 0151). One second by default.
    /// </summary>
    public TimeSpan RegexTimeout { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// §6.4's <c>subClassOfInShapesGraph</c>: read <c>rdfs:subClassOf</c>
    /// from the shapes graph as well as the data graph. False by default.
    /// </summary>
    public bool SubClassOfInShapesGraph { get; init; }

    /// <summary>
    /// §6.8.1.2's conformance-disallow set, or null for
    /// <see cref="DefaultConformanceDisallows"/>. A report states a set other
    /// than the default.
    /// </summary>
    public IReadOnlyList<RdfTerm>? ConformanceDisallows { get; init; }

    /// <summary>
    /// Refuse, at load, a constraint whose value depends on the clock or on
    /// chance — SPARQL's <c>NOW()</c>, <c>RAND()</c>, <c>UUID()</c> and
    /// <c>STRUUID()</c>, and <c>BNODE()</c> whose value can reach a result —
    /// since a validation that uses one cannot be repeated (ADR 0169). False
    /// by default; a shapes binding not <c>off</c> sets it.
    /// </summary>
    public bool RefuseNonDeterministicFunctions { get; init; }

    /// <summary>The RDFC-1.0 options that label the report's own blank nodes (ADR 0154).</summary>
    public CanonicalisationOptions Canonicalisation { get; init; } = CanonicalisationOptions.Default;
}
