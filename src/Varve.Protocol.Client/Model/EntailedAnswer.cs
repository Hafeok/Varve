// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Net.Http;

namespace Varve.Protocol.Client.Model;

/// <summary>
/// What a query's answer under <c>Varve-Entailment: owl2-rl</c> says of
/// itself in its headers (ADR 0197, the maintainer's ruling of 2026-11-19:
/// where an answer is narrower than its request suggests, it says so
/// in-band). Each is false on an answer under any other regime.
/// </summary>
public readonly record struct EntailedAnswer
{
    private EntailedAnswer(bool inconsistent, bool equalityNotApplied, bool perGraphClosure)
    {
        Inconsistent = inconsistent;
        EqualityNotApplied = equalityNotApplied;
        PerGraphClosure = perGraphClosure;
    }

    /// <summary><c>Varve-Inconsistent: true</c>: a graph the query read holds a clash, and the answer is the rules' fixpoint all the same (ADR 0197 point 5).</summary>
    public bool Inconsistent { get; }

    /// <summary>
    /// <c>Varve-Equality: not-applied</c>: the binding applies no equality
    /// rule (<c>varve:sameAs varve:SameAsOff</c>), so answers that need
    /// <c>owl:sameAs</c> substituted through are missing; the graph's
    /// service description names its profile as
    /// <c>varve:OWL2RLWithoutEquality</c>, not <c>pr:RL</c>.
    /// </summary>
    public bool EqualityNotApplied { get; }

    /// <summary>
    /// <c>Varve-Closure: per-graph</c>: the query's default graph merged two
    /// or more graphs, and was answered by the union of each graph's
    /// closure, not by the closure of their merge; a conclusion drawn from
    /// facts of two graphs together is missing unless a cross-graph
    /// binding's premise-set graph that holds it is named too.
    /// </summary>
    public bool PerGraphClosure { get; }

    /// <summary>What the headers of <paramref name="response"/> say of its answer.</summary>
    public static EntailedAnswer Of(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return new EntailedAnswer(
            Is(response, "Varve-Inconsistent", "true"),
            Is(response, "Varve-Equality", "not-applied"),
            Is(response, "Varve-Closure", "per-graph"));
    }

    private static bool Is(HttpResponseMessage response, string header, string value) =>
        response.Headers.TryGetValues(header, out IEnumerable<string>? values) && string.Join(",", values).Trim() == value;
}
