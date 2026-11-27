// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.WebUi.Components.Atoms;
using Varve.WebUi.Explorer.Atoms;

namespace Varve.WebUi.Explorer.Organisms;

/// <summary>One group of a <c>ValidationReport</c>: a shape or a focus node, and its results.</summary>
/// <param name="Title">The shape or the focus node the group is of.</param>
/// <param name="Count">How many results, in words.</param>
/// <param name="Caption">The table's caption, for screen readers.</param>
/// <param name="Rows">The results.</param>
public sealed record ReportGroup(TermValue Title, string Count, string Caption, IReadOnlyList<ReportRow> Rows);

/// <summary>One validation result.</summary>
/// <param name="Tone">The severity's tone, from <c>Tones</c>.</param>
/// <param name="Severity">The severity's word: violation, warning, info.</param>
/// <param name="Focus">The focus node.</param>
/// <param name="Path">The result path in SPARQL path syntax, or empty.</param>
/// <param name="Value">The value, when the result has one.</param>
/// <param name="Message">The result's message.</param>
public sealed record ReportRow(Tone Tone, string Severity, TermValue Focus, string Path, TermValue? Value, string Message);
