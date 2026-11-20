// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.WebUi.Components.Atoms;

namespace Varve.WebUi.Explorer.Atoms;

/// <summary>
/// One term as a <c>TermChip</c> shows it, for components that render a
/// list of them from data (a results table, a report): the chip's own
/// parameters, as a value.
/// </summary>
/// <param name="Kind">The term's kind.</param>
/// <param name="Text">What is shown: an IRI's compact or full form, a literal's lexical form, a blank node's label.</param>
/// <param name="Type">A literal's datatype or language, as its caption.</param>
/// <param name="Full">An IRI's full form, when <paramref name="Text"/> is compact.</param>
/// <param name="Href">Where an IRI leads.</param>
public sealed record TermValue(TermKind Kind, string Text, string? Type = null, string? Full = null, string? Href = null);
