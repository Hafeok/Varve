// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Xsd;

/// <summary>
/// The outcome of comparing two values under a partial order.
/// </summary>
/// <remarks>
/// XML Schema 1.1 defines <c>xsd:duration</c> (§3.3.6.1) and the date and
/// time types (§D.2.1, §E.3.4) as partially ordered: some pairs are neither
/// less, equal nor greater. A comparison that returned an <c>int</c> would
/// have to invent an order for them, and an evaluator that trusted it would
/// return an answer where SPARQL wants a type error.
/// </remarks>
public enum PartialOrdering
{
    /// <summary>The two values cannot be ordered.</summary>
    Indeterminate = 0,

    /// <summary>The first value is less than the second.</summary>
    Less = 1,

    /// <summary>The two values are equal.</summary>
    Equal = 2,

    /// <summary>The first value is greater than the second.</summary>
    Greater = 3,
}
