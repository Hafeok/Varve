// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation.Execution;

namespace Varve.Sparql.Evaluation.Operators;

/// <summary>
/// <c>SERVICE</c> through the options' <see cref="IServiceHandler"/> (ADR 0055,
/// <c>sparql-evaluation.md</c> §6.12): the handler is given the solutions the
/// answer will be joined with, and the evaluator joins. A failure without
/// <c>SILENT</c> fails the query naming the endpoint; with <c>SILENT</c> the
/// pattern is Ω0, one empty solution, per Federated Query §2.3.
/// </summary>
internal sealed class ServiceOperator : Operator
{
    private readonly Service _node;
    private readonly RdfTerm? _endpoint;
    private readonly int _endpointSlot;
    private readonly Variable[] _variables;
    private readonly int[] _slots;

    internal ServiceOperator(Service node, RdfTerm? endpoint, int endpointSlot, Variable[] variables, int[] slots)
    {
        _node = node;
        _endpoint = endpoint;
        _endpointSlot = endpointSlot;
        _variables = variables;
        _slots = slots;
    }

    internal override IEnumerator<ulong[]> Open(Exec exec, ulong[] input, ActiveGraph graph) =>
        Join(exec, Solutions.Once(input), graph);

    /// <summary>Joins the service's answer with the incoming solutions, invoking it once per endpoint.</summary>
    internal IEnumerator<ulong[]> Join(Exec exec, IEnumerator<ulong[]> incoming, ActiveGraph graph)
    {
        List<ulong[]> rows = [];
        using (incoming)
        {
            while (incoming.MoveNext())
            {
                rows.Add(incoming.Current);
            }
        }

        foreach ((RdfTerm? endpoint, List<ulong[]> group) in ByEndpoint(exec, rows))
        {
            string? failure = null;
            List<ulong[]>? answer = endpoint is null ? null : Invoke(exec, endpoint, group, out failure);
            if (answer is null)
            {
                if (!_node.Silent)
                {
                    string name = endpoint is null ? "an unbound or non-IRI endpoint" : "<" + Encoding.UTF8.GetString(endpoint.Lexical) + ">";
                    throw new QueryEvaluationException("SERVICE " + name + " failed: " + (endpoint is null ? "no IRI to call." : failure));
                }

                // Ω0 joined with each incoming solution is that solution.
                foreach (ulong[] row in group)
                {
                    yield return row;
                }

                continue;
            }

            foreach (ulong[] left in group)
            {
                foreach (ulong[] right in answer)
                {
                    if (Rows.Compatible(exec, left, right))
                    {
                        yield return Rows.Merge(exec.Width, left, right);
                    }
                }
            }
        }
    }

    private IEnumerable<(RdfTerm? Endpoint, List<ulong[]> Rows)> ByEndpoint(Exec exec, List<ulong[]> rows)
    {
        if (_endpoint is not null)
        {
            yield return (_endpoint, rows);
            yield break;
        }

        Dictionary<RdfTerm, List<ulong[]>> groups = new(RdfTerm.Comparer);
        List<RdfTerm> order = [];
        List<ulong[]> unusable = [];
        foreach (ulong[] row in rows)
        {
            TermRef value = Rows.Get(row, exec.Width, _endpointSlot);
            RdfTerm? iri = value.IsBound ? exec.Materialise(value) : null;
            if (iri is null || iri.Kind != RdfTermKind.Iri)
            {
                unusable.Add(row);
                continue;
            }

            if (!groups.TryGetValue(iri, out List<ulong[]>? group))
            {
                group = [];
                groups.Add(iri, group);
                order.Add(iri);
            }

            group.Add(row);
        }

        foreach (RdfTerm iri in order)
        {
            yield return (iri, groups[iri]);
        }

        if (unusable.Count > 0)
        {
            yield return (null, unusable);
        }
    }

    private List<ulong[]>? Invoke(Exec exec, RdfTerm endpoint, List<ulong[]> group, out string? failure)
    {
        List<IReadOnlyList<RdfTerm?>> incoming = new(group.Count);
        foreach (ulong[] row in group)
        {
            RdfTerm?[] terms = new RdfTerm?[_slots.Length];
            for (int i = 0; i < _slots.Length; i++)
            {
                TermRef value = Rows.Get(row, exec.Width, _slots[i]);
                terms[i] = value.IsBound ? exec.Materialise(value) : null;
            }

            incoming.Add(terms);
        }

        ServiceResult result;
        try
        {
            result = exec.Options.ServiceHandler.Execute(new ServiceRequest(endpoint, _node, _variables, incoming), exec.Token);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            failure = error.Message;
            return null;
        }

        if (result.IsFailure)
        {
            failure = result.Failure;
            return null;
        }

        // Map the answer's columns to this query's slots; a blank node from the
        // endpoint is a fresh node here, one per label per answer.
        int[] columns = new int[result.Variables.Count];
        for (int i = 0; i < columns.Length; i++)
        {
            columns[i] = Array.IndexOf(_variables, result.Variables[i]) is int at and >= 0 ? _slots[at] : -1;
        }

        Dictionary<RdfTerm, TermRef> blankNodes = new(RdfTerm.Comparer);
        List<ulong[]> answer = [];
        try
        {
            foreach (IReadOnlyList<RdfTerm?> solution in result.Solutions)
            {
                exec.Check();
                ulong[] row = exec.NewRow();
                for (int i = 0; i < columns.Length && i < solution.Count; i++)
                {
                    if (columns[i] < 0 || solution[i] is not { } term)
                    {
                        continue;
                    }

                    TermRef value;
                    if (term.Kind == RdfTermKind.BlankNode)
                    {
                        if (!blankNodes.TryGetValue(term, out value))
                        {
                            value = exec.InternLocal(exec.MintBlankNode());
                            blankNodes.Add(term, value);
                        }
                    }
                    else
                    {
                        value = exec.Intern(term);
                    }

                    Rows.Set(row, exec.Width, columns[i], value);
                }

                answer.Add(row);
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            failure = error.Message;
            return null;
        }

        failure = null;
        return answer;
    }
}
