// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Delta.Model;
using Varve.Events;
using Varve.Events.Model;
using Varve.Json.Canonical;
using Varve.Management;
using Varve.Management.Model;
using Varve.Protocol.Endpoints;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Protocol;

/// <summary>
/// The code validator of the system dataset (ADR 0166 point 4): every commit
/// to it is read as declarations, and a finding in a graph the commit touches
/// refuses it as <c>declaration-invalid</c>, naming the graph, the focus node
/// and the path. A quad outside the fleet's and the datasets' graphs is
/// refused, and so is any change to the bootstrap graph, which only the
/// system writer records, and not through this validator.
/// </summary>
[Contract(typeof(VarveManagementAndTheVocabulary.CodeValidatorNamesFocusAndPath), Role = "the system dataset's validator of management declarations")]
public sealed class ManagementValidator : ICommitValidator
{
    /// <inheritdoc />
    public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        HashSet<RdfTerm> touched = [];
        List<DeclarationFinding> findings = [];
        Touch(proposed, delta.Asserted, touched, findings);
        Touch(proposed, delta.Retracted, touched, findings);

        if (findings.Count == 0 && touched.Count > 0)
        {
            foreach (DeclarationFinding finding in DeclarationReader.Read(proposed).Findings)
            {
                if (touched.Contains(finding.Graph))
                {
                    findings.Add(finding);
                }
            }
        }

        if (findings.Count > 0)
        {
            return ValidationVerdict.Reject(Report(findings));
        }

        return Moves(proposed, delta, touched);
    }

    /// <summary>
    /// The event declarations of each dataset graph the commit touches (ADR
    /// 0167): the state's change is one the move allows, and a moved graph's
    /// event nodes, put in the catalogue graphs' form before and after, pass
    /// the same check as before the move (<see cref="EventDeclarationCheck"/>).
    /// </summary>
    private static ValidationVerdict Moves(IQuadSource proposed, QuadDelta delta, HashSet<RdfTerm> touched)
    {
        Dictionary<RdfTerm, (List<(RdfTerm, RdfTerm, RdfTerm)> Before, List<(RdfTerm, RdfTerm, RdfTerm)> After)> graphs = [];
        InMemoryDatasetBuilder before = new(), after = new();

        foreach (RdfTerm graph in touched)
        {
            if (!ManagementVocabulary.TryGetDataset(graph, out _))
            {
                continue;
            }

            List<(RdfTerm, RdfTerm, RdfTerm)> now = Triples(proposed, graph);
            HashSet<(RdfTerm, RdfTerm, RdfTerm)> then = [.. now];
            Undo(proposed, delta.Asserted, graph, then, remove: true);
            Undo(proposed, delta.Retracted, graph, then, remove: false);
            graphs[graph] = ([.. then], now);

            foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in then)
            {
                before.Add(s, p, o, graph);
            }

            foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in now)
            {
                after.Add(s, p, o, graph);
            }
        }

        if (graphs.Count == 0)
        {
            return ValidationVerdict.Accept();
        }

        Declarations previous = DeclarationReader.Read(before.ToDataset());
        Declarations next = DeclarationReader.Read(after.ToDataset());
        IReadOnlyList<DeclarationFinding> transitions = EventDeclarationMoves.Check(previous, next);

        if (transitions.Count > 0)
        {
            return ValidationVerdict.Reject(Report([.. transitions]));
        }

        foreach ((RdfTerm graph, (List<(RdfTerm, RdfTerm, RdfTerm)> then, List<(RdfTerm, RdfTerm, RdfTerm)> now)) in graphs)
        {
            ManagementVocabulary.TryGetDataset(graph, out string? name);

            if (!next.Datasets.TryGetValue(name!, out DatasetDeclaration? declared) || declared.EventDeclarations != EventDeclarationState.Moved)
            {
                continue;
            }

            List<DeclarationProblem> problems = [];
            List<FeedChange> moved = ManagedEventDeclarations.ToCatalogue(now, problems);

            if (problems.Count > 0)
            {
                return EventCatalogueValidator.Refuse(problems[0]);
            }

            List<FeedChange> changes = ManagedEventDeclarations.Changes(ManagedEventDeclarations.ToCatalogue(then, []), moved);

            if (changes.Count == 0)
            {
                continue;
            }

            InMemoryDatasetBuilder catalogue = new();

            foreach (FeedChange change in moved)
            {
                catalogue.Add(change.Subject, change.Predicate, change.Object, change.Graph);
            }

            DeclarationVerdict verdict = EventDeclarationCheck.Check(catalogue.ToDataset(), changes);

            if (!verdict.Accepted)
            {
                return EventCatalogueValidator.Refuse(verdict);
            }
        }

        return ValidationVerdict.Accept();
    }

    private static List<(RdfTerm, RdfTerm, RdfTerm)> Triples(IQuadSource source, RdfTerm graph)
    {
        List<(RdfTerm, RdfTerm, RdfTerm)> triples = [];

        if (!source.TryInternalise(graph, out TermHandle handle))
        {
            return triples;
        }

        using IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Named(handle));

        while (cursor.MoveNext())
        {
            Quad quad = cursor.Current;

            if (source.TryExternalise(quad.Subject, out RdfTerm? s)
                && source.TryExternalise(quad.Predicate, out RdfTerm? p)
                && source.TryExternalise(quad.Object, out RdfTerm? o))
            {
                triples.Add((s, p, o));
            }
        }

        return triples;
    }

    // The graph's triples before the commit: its assertions taken out, its retractions put back.
    private static void Undo(IQuadSource proposed, ReadOnlySpan<Quad> quads, RdfTerm graph, HashSet<(RdfTerm, RdfTerm, RdfTerm)> state, bool remove)
    {
        foreach (Quad quad in quads)
        {
            if (!quad.IsDefaultGraph
                && proposed.TryExternalise(quad.Graph, out RdfTerm? g) && g.Equals(graph)
                && proposed.TryExternalise(quad.Subject, out RdfTerm? s)
                && proposed.TryExternalise(quad.Predicate, out RdfTerm? p)
                && proposed.TryExternalise(quad.Object, out RdfTerm? o))
            {
                _ = remove ? state.Remove((s, p, o)) : state.Add((s, p, o));
            }
        }
    }

    private static void Touch(IQuadSource proposed, ReadOnlySpan<Quad> quads, HashSet<RdfTerm> touched, List<DeclarationFinding> findings)
    {
        foreach (Quad quad in quads)
        {
            if (quad.IsDefaultGraph || !proposed.TryExternalise(quad.Graph, out RdfTerm? graph))
            {
                findings.Add(new DeclarationFinding(RdfTerm.Iri("urn:x-varve:default-graph"u8), null, null,
                    "the system dataset's default graph holds nothing: every declaration is in a management graph"));
                return;
            }

            if (graph.Equals(ManagementVocabulary.BootstrapGraph))
            {
                findings.Add(new DeclarationFinding(graph, null, null, "the bootstrap graph is recorded by the system writer alone (ADR 0163 point 9)"));
                return;
            }

            if (!ManagementVocabulary.IsManagementGraph(graph))
            {
                findings.Add(new DeclarationFinding(graph, null, null, "the system dataset holds the fleet graph and the datasets' graphs, and nothing else"));
                return;
            }

            touched.Add(graph);
        }
    }

    private static IReadOnlyList<RdfTerm> Report(List<DeclarationFinding> findings)
    {
        ArrayBufferWriter<byte> json = new();
        CanonicalJsonWriter writer = new(json);
        writer.StartObject();
        writer.WriteName("detail"u8);
        writer.WriteString(findings[0].Message);
        writer.WriteName("findings"u8);
        writer.StartArray();

        foreach (DeclarationFinding finding in findings)
        {
            writer.StartObject();

            if (finding.Focus is { } focus)
            {
                writer.WriteName("focus"u8);
                writer.WriteString(focus.Lexical);
            }

            writer.WriteName("graph"u8);
            writer.WriteString(finding.Graph.Lexical);
            writer.WriteName("message"u8);
            writer.WriteString(finding.Message);

            if (finding.Path is { } path)
            {
                writer.WriteName("path"u8);
                writer.WriteString(path.Lexical);
            }

            writer.EndObject();
        }

        writer.EndArray();
        writer.EndObject();
        return ValidatorProblem.Report(ProblemType.DeclarationInvalid, json.WrittenSpan.ToArray());
    }
}
