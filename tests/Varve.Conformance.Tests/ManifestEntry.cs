// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Conformance.Tests;

/// <summary>What a manifest entry asserts about one input file.</summary>
internal enum ExpectedOutcome
{
    /// <summary>The file is well-formed and must parse.</summary>
    Parses,

    /// <summary>The file is ill-formed and must be rejected.</summary>
    IsRejected,

    /// <summary>
    /// The test is expected to parse and to produce the dataset in its
    /// <c>mf:result</c>, up to a bijection of blank nodes.
    /// </summary>
    /// <remarks>
    /// The comparison is not implemented — it is milestone 3b's isomorphism
    /// step (ADR 0030 §3). Until it is, an entry of this kind may be read, so
    /// that the chunk-boundary oracle has the input as corpus, but it may not
    /// appear in a ratcheted suite: recording it as passing on the strength of
    /// "it parsed" would claim conformance the harness has not checked, and
    /// the guard test is what stops that happening by accident.
    /// </remarks>
    Evaluates,
}

/// <summary>
/// One entry from a W3C manifest, resolved to a file on disk.
/// </summary>
/// <param name="TestIri">
/// The entry's IRI, resolved against the suite's published base. This is the
/// test's identity: it names the case in the run, and it is what a line in
/// <c>baseline/passing.txt</c> refers to.
/// </param>
/// <param name="Suite">
/// The <see cref="ConformanceSuite.Id"/> this entry was read from. Carried on
/// the entry rather than recovered from the IRI: the rdf11 and rdf12 suites for
/// one format have IRIs that differ only in a middle segment, and grouping by
/// string prefix got that wrong once already.
/// </param>
/// <param name="Name">The entry's <c>mf:name</c>.</param>
/// <param name="Comment">The entry's <c>rdfs:comment</c>, if it has one.</param>
/// <param name="ActionPath">The absolute path of the file to parse.</param>
/// <param name="ActionIri">
/// The IRI the file is published at, which is the base a relative reference in
/// it resolves against. Several suite inputs have no <c>@base</c> and relative
/// IRIs throughout, so without it they do not parse at all.
/// </param>
/// <param name="Format">The syntax to parse it as.</param>
/// <param name="Expected">Whether parsing must succeed or must fail.</param>
/// <param name="ResultPath">
/// The <c>mf:result</c> dataset an evaluation test expects, or null. Read now
/// and compared at the isomorphism step; carrying it early is what makes the
/// guard against ratcheting an unchecked evaluation possible.
/// </param>
internal sealed record ManifestEntry(
    string TestIri,
    string Suite,
    string Name,
    string? Comment,
    string ActionPath,
    string ActionIri,
    RdfFormat Format,
    ExpectedOutcome Expected,
    string? ResultPath = null);
