// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Net.Http.Headers;
using Varve.Rdf;

namespace Varve.Protocol.Client.Model;

/// <summary>How a query is read: where it is pinned and under which regime (ADRs 0096, 0192).</summary>
/// <param name="AsOf">Where the read is pinned.</param>
/// <param name="Entailment">The regime to answer under, or null for the dataset's default.</param>
public sealed record QueryReadOptions(AsOfSelector AsOf, Entailment? Entailment = null);

/// <summary>What a query's answer is, read from its media type.</summary>
public enum QueryForm
{
    /// <summary>Solutions: a SELECT.</summary>
    Solutions,

    /// <summary>A boolean: an ASK.</summary>
    Boolean,

    /// <summary>A graph: a CONSTRUCT or a DESCRIBE.</summary>
    Graph,
}

/// <summary>How a streamed answer ended.</summary>
public enum QueryEnd
{
    /// <summary>It is still being read.</summary>
    Reading,

    /// <summary>The server finished it.</summary>
    Complete,

    /// <summary>The reader closed it before the end: the row ceiling, a cancel, or leaving the view.</summary>
    Closed,

    /// <summary>
    /// It ended before the server finished it: a limit reached after the first
    /// flush, whose problem travels in a trailer a browser cannot read (#351).
    /// </summary>
    Cut,
}

/// <summary>One solution: a term per variable, in the answer's order, null where unbound.</summary>
/// <param name="Values">The terms.</param>
public sealed record Solution(IReadOnlyList<RdfTerm?> Values);

/// <summary>One statement of a graph answer.</summary>
/// <param name="Subject">The subject.</param>
/// <param name="Predicate">The predicate.</param>
/// <param name="Object">The object.</param>
public sealed record Statement(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object);

/// <summary>An ASK query's boolean.</summary>
public enum AskResult
{
    /// <summary>No solution exists, among what the caller reads.</summary>
    False,

    /// <summary>A solution exists.</summary>
    True,
}

/// <summary>An ASK query's answer.</summary>
/// <param name="Result">The boolean.</param>
/// <param name="Entailment">The regime that answered, as <c>Varve-Entailment</c> echoed it.</param>
public sealed record AskAnswer(AskResult Result, Entailment? Entailment);

/// <summary>What a download wrote.</summary>
/// <param name="MediaType">The format the server wrote.</param>
public sealed record Download(MediaTypeHeaderValue? MediaType);

/// <summary>
/// What the dataset's service description says a reader may use (SPARQL 1.1
/// Service Description; ADRs 0192 point 5, 0237 point 3): the graphs the
/// caller's grants let it read, the regimes per graph, the default regime,
/// the as-of distance limit, and the validation report graphs among the
/// graphs, by binding (ADR 0174 point 8).
/// </summary>
/// <param name="Graphs">The named graphs readable by the caller, as the description lists them.</param>
/// <param name="DefaultEntailment">The default regime's IRI.</param>
/// <param name="Regimes">For each graph a binding covers, the regimes served over it, by IRI.</param>
/// <param name="MaxAsOfDistance">How far below the head an as-of read may be, when stated.</param>
/// <param name="ReportGraphs">The validation report graphs among <paramref name="Graphs"/>, by their binding's IRI.</param>
public sealed record DatasetDescription(
    IReadOnlyList<RdfTerm> Graphs,
    RdfTerm? DefaultEntailment,
    IReadOnlyDictionary<RdfTerm, IReadOnlyList<RdfTerm>> Regimes,
    AsOfDistance? MaxAsOfDistance,
    IReadOnlyDictionary<RdfTerm, RdfTerm> ReportGraphs);
