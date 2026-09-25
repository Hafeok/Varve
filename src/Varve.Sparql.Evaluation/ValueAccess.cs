// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Sparql.Evaluation;

/// <summary>
/// How an expression obtains the value of a term the source holds: the three
/// arms of ADR 0050's benchmark (<c>sparql-evaluation.md</c> §10).
/// </summary>
public enum ValueAccess : byte
{
    /// <summary>
    /// Ask the source for the value its handle encodes
    /// (<c>IQuadSource.TryGetInlineValue</c>), and externalise only when it has
    /// none. The default.
    /// </summary>
    InlineAccessor,

    /// <summary>Externalise the term and parse its lexical form, always: the handle contract as ADR 0022 shipped it.</summary>
    Externalise,

    /// <summary>
    /// Externalise every term a scan binds, at the scan, and read owned terms
    /// thereafter: the term-based contract ADR 0022 rejected, approximated
    /// inside the evaluator. Joins still compare handles — here, indexes into
    /// the execution's own term table — so this arm's cost is a lower bound on
    /// that contract's.
    /// </summary>
    Materialise,
}
