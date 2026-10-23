// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Rdf;
using Varve.Shacl.Validation;

namespace Varve.Shacl.Incremental;

/// <summary>
/// A result's identity for ADR 0169's R1: focus node, source shape, source
/// constraint component, path by its SPARQL form, and value, as terms, so
/// that a result before a delta and one after it compare.
/// </summary>
internal readonly record struct ResultIdentity(RdfTerm Focus, RdfTerm Shape, RdfTerm Component, string Path, RdfTerm? Value)
{
    internal static ResultIdentity Of(DataGraph data, in ResultRecord record) => new(
        data.Term(record.Focus),
        record.Shape.Node,
        record.Component,
        record.Path?.Sparql ?? string.Empty,
        record.Value is { } value ? data.Term(value) : null);
}
