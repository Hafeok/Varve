// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.WebUi.Organisms;

/// <summary>
/// One coloured run of the query editor's text (docs/ui/components/QueryEditor):
/// a range of the text, in UTF-16 code units, and the <c>vw-tok--</c> class
/// it takes. The organism knows no tokenizer (ADR 0206 point 4): a part maps
/// tokens to runs.
/// </summary>
/// <param name="Start">The first code unit of the run.</param>
/// <param name="End">The code unit after the run.</param>
/// <param name="Kind">The class suffix: <c>keyword</c>, <c>iri</c>, <c>bnode</c>, <c>variable</c>, <c>literal</c>, <c>annotation</c>, <c>punct</c>, <c>comment</c> or <c>word</c>.</param>
public sealed record EditorRun(int Start, int End, string Kind);

/// <summary>
/// The parse error the editor shows: where it is, in UTF-16 code units, and
/// its message for the field's error slot. At the end of the text it marks a
/// space after it; elsewhere it marks the run that starts at or holds it.
/// </summary>
/// <param name="Offset">Where the error is.</param>
/// <param name="Message">What is wrong, as the field's error line says it.</param>
public sealed record EditorError(int Offset, string Message);
