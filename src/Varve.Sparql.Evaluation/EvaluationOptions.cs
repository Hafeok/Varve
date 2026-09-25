// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace Varve.Sparql.Evaluation;

/// <summary>
/// Everything the evaluator takes from outside (ADR 0056,
/// <c>sparql-evaluation.md</c> §3). Immutable; nothing here has an ambient
/// default — a query that needs the clock or randomness and was not given one
/// fails, naming the option.
/// </summary>
public sealed class EvaluationOptions
{
    /// <summary>Options with every default.</summary>
    public static EvaluationOptions Default { get; } = new();

    /// <summary>
    /// Whether to apply the optimiser (ADR 0048). The property-path
    /// normalisation of ADR 0054 runs either way.
    /// </summary>
    public bool Optimise { get; init; } = true;

    /// <summary>How an expression obtains a term's value: ADR 0050's three arms.</summary>
    public ValueAccess ValueAccess { get; init; } = ValueAccess.InlineAccessor;

    /// <summary>
    /// The implicit timezone of XPath Functions and Operators §10.4, in
    /// minutes east of UTC, supplied to a dateTime without one when it is
    /// compared (ADR 0051). Defaults to UTC.
    /// </summary>
    public int ImplicitTimezoneOffsetMinutes { get; init; }

    /// <summary>
    /// The clock <c>NOW()</c> reads, once per execution. None by default; the
    /// caller's line is <c>Clock = TimeProvider.System</c>.
    /// </summary>
    public TimeProvider? Clock { get; init; }

    /// <summary>Randomness for <c>RAND()</c>, <c>UUID()</c>, <c>STRUUID()</c>. None by default.</summary>
    public IRandomSource? Randomness { get; init; }

    /// <summary>Evaluates <c>SERVICE</c> (ADR 0055). By default, refuses.</summary>
    public IServiceHandler ServiceHandler { get; init; } = RefusingServiceHandler.Instance;

    /// <summary>Extension functions, by IRI (§17.6).</summary>
    public IReadOnlyDictionary<string, IExtensionFunction>? Functions { get; init; }

    /// <summary>Custom aggregates, by IRI (ADR 0053).</summary>
    public IReadOnlyDictionary<string, IExtensionAggregate>? Aggregates { get; init; }

    /// <summary>The longest one <c>REGEX</c> or <c>REPLACE</c> match may take; longer is an expression error.</summary>
    public TimeSpan RegexTimeout { get; init; } = TimeSpan.FromSeconds(1);
}
