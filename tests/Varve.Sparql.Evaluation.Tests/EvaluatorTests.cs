// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Xunit;
using static Varve.Sparql.Evaluation.Tests.Support;

namespace Varve.Sparql.Evaluation.Tests;

/// <summary>The evaluator's contract beyond the W3C suites: options, cancellation, SERVICE.</summary>
public class EvaluatorTests
{
    private static InMemoryDataset Chain(int length)
    {
        InMemoryDataset dataset = new();
        for (int i = 0; i < length; i++)
        {
            dataset.Add(Named("n" + i), Named("next"), Named("n" + (i + 1)));
        }

        return dataset;
    }

    [Fact]
    public void A_token_cancelled_before_the_call_stops_it() =>
        Assert.ThrowsAny<OperationCanceledException>(() =>
            new SparqlEvaluator(Options()).Evaluate(Parse("SELECT * { ?s ?p ?o }"), Chain(3), new CancellationToken(true)));

    [Fact]
    public void A_token_cancelled_while_the_query_runs_stops_it()
    {
        using CancellationTokenSource cancellation = new();
        CancellingSource source = new(Chain(200), cancellation, after: 500);

        // A cross product of 200 × 200 × 200: without the check it runs for 8 million rows.
        using QueryResults results = new SparqlEvaluator(Options()).Evaluate(Parse("SELECT * { ?a :next ?b . ?c :next ?d . ?e :next ?f }"), source, cancellation.Token);
        SolutionResults solutions = (SolutionResults)results;
        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            while (solutions.MoveNext())
            {
            }
        });
    }

    [Fact]
    public void A_closure_over_a_long_chain_notices_cancellation()
    {
        using CancellationTokenSource cancellation = new();
        CancellingSource source = new(Chain(5_000), cancellation, after: 2_000);
        using QueryResults results = new SparqlEvaluator(Options()).Evaluate(Parse("SELECT * { ?a :next+ ?b }"), source, cancellation.Token);
        SolutionResults solutions = (SolutionResults)results;
        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            while (solutions.MoveNext())
            {
            }
        });
    }

    [Fact]
    public void Now_without_a_clock_fails_naming_the_option()
    {
        QueryEvaluationException error = Assert.Throws<QueryEvaluationException>(() =>
            Run("SELECT (NOW() AS ?t) {}", Chain(1), new EvaluationOptions()));
        Assert.Contains("EvaluationOptions.Clock", error.Message, StringComparison.Ordinal);
        Assert.Contains("TimeProvider.System", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("RAND()")]
    [InlineData("UUID()")]
    [InlineData("STRUUID()")]
    public void Randomness_without_a_source_fails_naming_the_option(string call)
    {
        QueryEvaluationException error = Assert.Throws<QueryEvaluationException>(() =>
            Run("SELECT (" + call + " AS ?r) {}", Chain(1), new EvaluationOptions()));
        Assert.Contains("EvaluationOptions.Randomness", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Now_is_the_injected_clock_and_one_instant_per_query()
    {
        List<string> rows = Run("SELECT (NOW() AS ?t) (NOW() = NOW() AS ?same) {}", Chain(1));
        Assert.Equal(["?t=\"2026-09-25T12:00:00Z\"^^<http://www.w3.org/2001/XMLSchema#dateTime> ?same=\"true\"^^<http://www.w3.org/2001/XMLSchema#boolean>"], rows);
    }

    [Fact]
    public void Service_is_refused_by_default()
    {
        QueryEvaluationException error = Assert.Throws<QueryEvaluationException>(() =>
            Run("SELECT * { SERVICE <http://example.org/sparql> { ?s ?p ?o } }", Chain(1)));
        Assert.Contains("ServiceHandler", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Federated Query §2.3: a failed SILENT service is the one solution that binds nothing.</summary>
    [Fact]
    public void A_silent_service_that_fails_is_the_empty_solution()
    {
        List<string> rows = Run("SELECT * { ?s :next ?o SERVICE SILENT <http://example.org/sparql> { ?x ?y ?z } }", Chain(2));
        Assert.Equal(["?s=<http://example.org/n0> ?o=<http://example.org/n1>", "?s=<http://example.org/n1> ?o=<http://example.org/n2>"], rows);
    }

    [Fact]
    public void A_service_handler_sees_the_endpoint_and_its_solutions_join()
    {
        RecordingHandler handler = new();
        EvaluationOptions options = new() { ServiceHandler = handler };
        List<string> rows = Run("SELECT * { ?s :next ?o SERVICE :remote { ?o :label ?l } }", Chain(2), options);
        Assert.Equal("http://example.org/remote", System.Text.Encoding.UTF8.GetString(handler.Endpoint!.Lexical));
        Assert.Equal(["?s=<http://example.org/n0> ?o=<http://example.org/n1> ?l=\"one\""], rows);
    }

    [Fact]
    public void An_extension_function_is_found_through_the_options_and_an_unknown_one_is_an_error()
    {
        EvaluationOptions options = new()
        {
            Functions = new Dictionary<string, IExtensionFunction> { [Ex + "twice"] = new Twice() },
        };
        Assert.Equal(["?r=\"abab\""], Run("SELECT (:twice(\"ab\") AS ?r) {}", Chain(1), options));

        // An unknown function is an expression error (§17.6): the variable is left unbound.
        Assert.Equal([""], Run("SELECT (:unknown(\"ab\") AS ?r) {}", Chain(1), options));
    }

    [Fact]
    public void The_optimiser_can_be_switched_off_and_the_answer_is_the_same()
    {
        const string query = "SELECT * { ?a :next ?b . ?b :next ?c FILTER(?a != :n0) }";
        Assert.Equal(Run(query, Chain(4), Options(optimise: true)), Run(query, Chain(4), Options(optimise: false)));
    }

    /// <summary>
    /// §18.4 defines a negated property set as a set, <c>{ μ | ∃ triple … }</c>:
    /// two triples between the same nodes are one solution, whichever ends are
    /// bound. The optimiser property found the free-ends scan counting both,
    /// which made the answer depend on join order.
    /// </summary>
    [Fact]
    public void A_negated_property_set_is_a_set_whichever_ends_are_bound()
    {
        InMemoryDataset dataset = new();
        dataset.Add(Named("s"), Named("p1"), Named("o"));
        dataset.Add(Named("s"), Named("p2"), Named("o"));
        dataset.Add(Named("s"), Named("p1"), Named("s"));
        dataset.Add(Named("s"), Named("p2"), Named("s"));

        Assert.Equal(2, Run("SELECT * { ?x !:p0 ?y }", dataset).Count);
        Assert.Equal(2, Run("SELECT * { :s !:p0 ?y }", dataset).Count);
        Assert.Single(Run("SELECT * { ?x !:p0 :o }", dataset));
        Assert.Single(Run("SELECT * { :s !:p0 :o }", dataset));
        Assert.Single(Run("SELECT * { ?x !:p0 ?x }", dataset));

        // !(a|^b) is alt(NPS(a), inv(NPS(b))): the halves are a multiset union.
        Assert.Equal(2, Run("SELECT * { :s !(:p0|^:p0) :s }", dataset).Count);
    }

    private sealed class Twice : IExtensionFunction
    {
        public bool TryEvaluate(ReadOnlySpan<RdfTerm> arguments, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RdfTerm? result)
        {
            if (arguments.Length != 1 || arguments[0].Kind != RdfTermKind.Literal)
            {
                result = null;
                return false;
            }

            byte[] lexical = [.. arguments[0].Lexical, .. arguments[0].Lexical];
            result = RdfTerm.Literal(lexical);
            return true;
        }
    }

    private sealed class RecordingHandler : IServiceHandler
    {
        internal RdfTerm? Endpoint { get; private set; }

        public ServiceResult Execute(ServiceRequest request, CancellationToken cancellationToken)
        {
            Endpoint = request.Endpoint;
            return ServiceResult.FromSolutions(
                [new Variable("o"), new Variable("l")],
                [[Named("n1"), RdfTerm.Literal("one"u8.ToArray())], [Named("n9"), RdfTerm.Literal("nine"u8.ToArray())]]);
        }
    }
}
