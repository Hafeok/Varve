// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Management.Model;
using Varve.Rdf;

namespace Varve.Management;

/// <summary>
/// What <c>varve apply</c> would change (ADR 0166 point 6): for each owned
/// graph, the diff of canonical N-Quads after RDFC-1.0, made at a system
/// position and against each graph's version. Saved as JSON; applied by
/// sending each graph's whole content under <c>Varve-If-Graphs-Match</c> at
/// those versions, so a plan whose graphs have moved is refused.
/// </summary>
[Contract(typeof(VarveManagementAndTheVocabulary.PlanIsCanonicalDiff), Role = "the plan varve apply shows and commits")]
public sealed class ManagementPlan
{
    private const int Format = 1;

    /// <summary>A plan.</summary>
    public ManagementPlan(ManagementPosition position, IReadOnlyList<GraphPlan> graphs)
    {
        ArgumentNullException.ThrowIfNull(graphs);
        Position = position;
        Graphs = graphs;
    }

    /// <summary>The system dataset's position the plan was made at.</summary>
    public ManagementPosition Position { get; }

    /// <summary>The owned graphs, in IRI order.</summary>
    public IReadOnlyList<GraphPlan> Graphs { get; }

    /// <summary>
    /// Every graph the desired declarations' TBox bindings make dataset-public,
    /// and every graph they stop making so, by dataset and then graph (ADR
    /// 0194 point 6). What the apply widens is shown before it is applied.
    /// </summary>
    public IReadOnlyList<DatasetPublicGraph> DatasetPublic { get; init; } = [];

    /// <summary>Whether applying it would change nothing.</summary>
    public bool IsEmpty
    {
        get
        {
            foreach (GraphPlan graph in Graphs)
            {
                if (graph.Added.Count > 0 || graph.Removed.Count > 0)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// The plan of <paramref name="owned"/>: their content in
    /// <paramref name="desired"/> against their content in
    /// <paramref name="current"/>, each canonicalised with RDFC-1.0.
    /// </summary>
    /// <param name="desired">The declaration files' quads.</param>
    /// <param name="owned">The graphs the files own, whole.</param>
    /// <param name="current">The system dataset at <paramref name="position"/>.</param>
    /// <param name="versions">Each owned graph's version at that position; a graph absent here is at 0.</param>
    /// <param name="position">The system dataset's position.</param>
    public static ManagementPlan Compute(IQuadSource desired, IEnumerable<RdfTerm> owned, IQuadSource current, IReadOnlyDictionary<RdfTerm, ManagementPosition> versions, ManagementPosition position)
    {
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(owned);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(versions);
        SortedSet<RdfTerm> graphs = new(owned, TermOrder.Instance);
        List<GraphPlan> plans = [];

        foreach (RdfTerm graph in graphs)
        {
            List<string> want = CanonicalLines(desired, graph);
            List<string> have = CanonicalLines(current, graph);
            HashSet<string> wanted = new(want, StringComparer.Ordinal);
            HashSet<string> had = new(have, StringComparer.Ordinal);
            plans.Add(new GraphPlan(
                graph,
                versions.TryGetValue(graph, out ManagementPosition version) ? version : default,
                want,
                want.FindAll(line => !had.Contains(line)),
                have.FindAll(line => !wanted.Contains(line))));
        }

        return new ManagementPlan(position, plans) { DatasetPublic = DatasetPublicChanges(desired, graphs, current) };
    }

    /// <summary>
    /// The dataset-public graphs under the declarations of
    /// <paramref name="current"/> with <paramref name="owned"/> replaced by
    /// their content in <paramref name="desired"/>, against those of
    /// <paramref name="current"/>.
    /// </summary>
    private static List<DatasetPublicGraph> DatasetPublicChanges(IQuadSource desired, SortedSet<RdfTerm> owned, IQuadSource current)
    {
        SortedDictionary<RdfTerm, List<DeclarationReader.Triple>> before = DeclarationReader.Graphs(current);
        SortedDictionary<RdfTerm, List<DeclarationReader.Triple>> after = new(before, TermOrder.Instance);
        SortedDictionary<RdfTerm, List<DeclarationReader.Triple>> wanted = DeclarationReader.Graphs(desired);

        foreach (RdfTerm graph in owned)
        {
            if (wanted.TryGetValue(graph, out List<DeclarationReader.Triple>? triples))
            {
                after[graph] = triples;
            }
            else
            {
                after.Remove(graph);
            }
        }

        SortedDictionary<string, SortedSet<RdfTerm>> had = PublicByDataset(DeclarationReader.Read(before));
        SortedDictionary<string, SortedSet<RdfTerm>> will = PublicByDataset(DeclarationReader.Read(after));
        SortedSet<string> datasets = new(had.Keys, StringComparer.Ordinal);
        datasets.UnionWith(will.Keys);
        List<DatasetPublicGraph> changes = [];

        foreach (string dataset in datasets)
        {
            SortedSet<RdfTerm> was = had.TryGetValue(dataset, out SortedSet<RdfTerm>? a) ? a : new(TermOrder.Instance);
            SortedSet<RdfTerm> now = will.TryGetValue(dataset, out SortedSet<RdfTerm>? b) ? b : new(TermOrder.Instance);
            SortedSet<RdfTerm> all = new(was, TermOrder.Instance);
            all.UnionWith(now);

            foreach (RdfTerm graph in all)
            {
                DatasetPublicChange change = !now.Contains(graph) ? DatasetPublicChange.Narrowed
                    : was.Contains(graph) ? DatasetPublicChange.Unchanged : DatasetPublicChange.Widened;
                changes.Add(new DatasetPublicGraph(dataset, graph, change));
            }
        }

        return changes;

        static SortedDictionary<string, SortedSet<RdfTerm>> PublicByDataset(Declarations declarations)
        {
            SortedDictionary<string, SortedSet<RdfTerm>> map = new(StringComparer.Ordinal);

            foreach ((string name, DatasetDeclaration dataset) in declarations.Datasets)
            {
                IReadOnlyList<RdfTerm> graphs = GrantTable.DatasetPublicGraphs(dataset);

                if (graphs.Count > 0)
                {
                    map[name] = new SortedSet<RdfTerm>(graphs, TermOrder.Instance);
                }
            }

            return map;
        }
    }

    /// <summary>The graphs a source names, which a declaration file owns.</summary>
    public static IReadOnlyList<RdfTerm> NamedGraphs(IQuadSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        SortedSet<RdfTerm> graphs = new(TermOrder.Instance);
        HashSet<TermHandle> seen = new(source.TermComparer);
        using IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.AnyNamed);

        while (cursor.MoveNext())
        {
            if (seen.Add(cursor.Current.Graph) && source.TryExternalise(cursor.Current.Graph, out RdfTerm? graph))
            {
                graphs.Add(graph);
            }
        }

        return [.. graphs];
    }

    /// <summary>One graph's quads in <paramref name="source"/>, canonicalised with RDFC-1.0, as N-Quads lines without their line ends.</summary>
    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public static List<string> CanonicalLines(IQuadSource source, RdfTerm graph)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(graph);
        InMemoryDatasetBuilder builder = new();

        if (source.TryInternalise(graph, out TermHandle handle))
        {
            using IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Named(handle));

            while (cursor.MoveNext())
            {
                Quad quad = cursor.Current;

                if (source.TryExternalise(quad.Subject, out RdfTerm? s)
                    && source.TryExternalise(quad.Predicate, out RdfTerm? p)
                    && source.TryExternalise(quad.Object, out RdfTerm? o))
                {
                    builder.Add(s, p, o, graph);
                }
            }
        }

        string text = Encoding.UTF8.GetString(RdfCanonicaliser.Canonicalise(builder.ToDataset()).NQuads.Span);
        List<string> lines = [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries)];
        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    /// <summary>The plan as an operator reads it: per graph, its version and its added and removed lines.</summary>
    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public string Render()
    {
        StringBuilder text = new();
        text.Append("System position ").Append(Position).Append('\n');

        // What the apply widens is shown before what it changes (ADR 0190 point 5).
        if (DatasetPublic.Count > 0)
        {
            text.Append("Dataset-public graphs, read by every reader of their dataset:\n");

            foreach (DatasetPublicGraph graph in DatasetPublic)
            {
                text.Append("  ").Append(graph.Dataset).Append(" <").Append(Encoding.UTF8.GetString(graph.Graph.Lexical)).Append('>');
                text.Append(graph.Change switch
                {
                    DatasetPublicChange.Widened => " (widened: dataset-public after this apply)\n",
                    DatasetPublicChange.Narrowed => " (narrowed: no longer dataset-public after this apply)\n",
                    _ => "\n",
                });
            }
        }

        foreach (GraphPlan graph in Graphs)
        {
            text.Append('<').Append(Encoding.UTF8.GetString(graph.Graph.Lexical)).Append("> at version ").Append(graph.Version);
            text.Append(graph.Added.Count == 0 && graph.Removed.Count == 0 ? ": no change\n" : ":\n");

            foreach (string line in graph.Removed)
            {
                text.Append("- ").Append(line).Append('\n');
            }

            foreach (string line in graph.Added)
            {
                text.Append("+ ").Append(line).Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>The plan as JSON, to save and apply later.</summary>
    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public byte[] ToJson()
    {
        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter json = new(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteNumber("format", Format);
            json.WriteNumber("position", Position.Value);
            json.WriteStartArray("graphs");

            foreach (GraphPlan graph in Graphs)
            {
                json.WriteStartObject();
                json.WriteString("graph", Encoding.UTF8.GetString(graph.Graph.Lexical));
                json.WriteNumber("version", graph.Version.Value);
                Lines(json, "content", graph.Content);
                Lines(json, "added", graph.Added);
                Lines(json, "removed", graph.Removed);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteStartArray("datasetPublic");

            foreach (DatasetPublicGraph graph in DatasetPublic)
            {
                json.WriteStartObject();
                json.WriteString("dataset", graph.Dataset);
                json.WriteString("graph", Encoding.UTF8.GetString(graph.Graph.Lexical));
                json.WriteString("change", graph.Change switch
                {
                    DatasetPublicChange.Widened => "widened",
                    DatasetPublicChange.Narrowed => "narrowed",
                    _ => "unchanged",
                });
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();

        static void Lines(Utf8JsonWriter json, string name, IReadOnlyList<string> lines)
        {
            json.WriteStartArray(name);

            foreach (string line in lines)
            {
                json.WriteStringValue(line);
            }

            json.WriteEndArray();
        }
    }

    /// <summary>Reads a saved plan.</summary>
    /// <exception cref="InvalidDataException">The bytes are not a plan this version reads.</exception>
    public static ManagementPlan FromJson(ReadOnlySpan<byte> utf8)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8.ToArray());
            JsonElement root = document.RootElement;

            if (root.GetProperty("format").GetInt32() != Format)
            {
                throw new InvalidDataException("The plan is of format " + root.GetProperty("format").GetInt32() + "; this version of Varve reads format " + Format + ".");
            }

            List<GraphPlan> graphs = [];

            foreach (JsonElement graph in root.GetProperty("graphs").EnumerateArray())
            {
                graphs.Add(new GraphPlan(
                    RdfTerm.Iri(Encoding.UTF8.GetBytes(graph.GetProperty("graph").GetString()!)),
                    new ManagementPosition(graph.GetProperty("version").GetInt64()),
                    Lines(graph, "content"),
                    Lines(graph, "added"),
                    Lines(graph, "removed")));
            }

            // A plan saved before ADR 0194 has no list: it was made with no TBox binding in effect.
            List<DatasetPublicGraph> datasetPublic = [];

            if (root.TryGetProperty("datasetPublic", out JsonElement list))
            {
                foreach (JsonElement graph in list.EnumerateArray())
                {
                    datasetPublic.Add(new DatasetPublicGraph(
                        graph.GetProperty("dataset").GetString()!,
                        RdfTerm.Iri(Encoding.UTF8.GetBytes(graph.GetProperty("graph").GetString()!)),
                        graph.GetProperty("change").GetString() switch
                        {
                            "widened" => DatasetPublicChange.Widened,
                            "narrowed" => DatasetPublicChange.Narrowed,
                            "unchanged" => DatasetPublicChange.Unchanged,
                            var other => throw new FormatException("A dataset-public change is widened, narrowed or unchanged, not " + other + "."),
                        }));
                }
            }

            return new ManagementPlan(new ManagementPosition(root.GetProperty("position").GetInt64()), graphs) { DatasetPublic = datasetPublic };
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
        {
            throw new InvalidDataException("The plan does not read: " + error.Message, error);
        }

        static List<string> Lines(JsonElement graph, string name)
        {
            List<string> lines = [];

            foreach (JsonElement line in graph.GetProperty(name).EnumerateArray())
            {
                lines.Add(line.GetString()!);
            }

            return lines;
        }
    }
}
