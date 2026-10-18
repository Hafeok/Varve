// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Evaluation.Model;
using Varve.Store;
using Varve.Store.Log;
using Varve.Turtle;

namespace BlendSpike;

/// <summary>
/// The spike's run, end to end, against an embedded in-memory Varve dataset:
/// the ontology as the first commit; the code facts and the decision ledger
/// projected at every first-parent commit of HEAD that touched a projected
/// file, one Varve commit each; the rules by level; the emergent check; the
/// reports; the as-of question.
/// </summary>
internal static class Program
{
    private const string Blends = "urn:varve-spike:g/blends";
    private const string Emergent = "urn:varve-spike:g/emergent";
    private const string Rules = "urn:varve-spike:g/rules";

    private static readonly string[] Pathspecs =
    [
        "*.csproj", "Directory.Build.props", "Directory.Packages.props",
        "*PublicAPI.Shipped.txt", "*PublicAPI.Unshipped.txt", "docs/decisions/*.md",
    ];

    private static readonly SparqlEvaluator Evaluator = new(new EvaluationOptions { Clock = TimeProvider.System });

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "query")
        {
            // query <dataset directory> <prefixes file> <query file>: one
            // query at the head of a dataset an earlier run wrote.
            await using FileStorage existing = await FileStorage.OpenAsync(new DatasetDirectory(args[1]), new FileStorageOptions { Clock = TimeProvider.System });
            await using Dataset opened = await Dataset.OpenAsync(existing, new DatasetOptions { Clock = TimeProvider.System });
            using DatasetView pinned = opened.Pin();
            Console.Write(Select(pinned, await File.ReadAllTextAsync(args[2]) + await File.ReadAllTextAsync(args[3])));
            return 0;
        }

        if (args.Length != 3)
        {
            Console.Error.WriteLine("usage: BlendSpike <repository root> <spike directory> <new dataset directory>");
            Console.Error.WriteLine("       BlendSpike query <dataset directory> <prefixes file> <query file>");
            return 2;
        }

        string repository = Path.GetFullPath(args[0]);
        string spike = Path.GetFullPath(args[1]);
        string output = Path.Combine(spike, "out");
        Directory.CreateDirectory(output);
        GitRepository git = new(repository);
        string prefixes = await File.ReadAllTextAsync(Path.Combine(spike, "rules", "_prefixes.rq"));

        Directory.CreateDirectory(args[2]);
        await using FileStorage storage = await FileStorage.OpenAsync(new DatasetDirectory(Path.GetFullPath(args[2])), new FileStorageOptions { Clock = TimeProvider.System });
        await using Dataset dataset = await Dataset.CreateAsync(storage, new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System });
        StringBuilder log = new();

        // 1. The ontology: vocabulary, domain terms, spaces, rules with their
        //    text, attempts. One commit.
        List<RdfQuad> ontology = [];
        ontology.AddRange(ParseTrig(await File.ReadAllTextAsync(Path.Combine(spike, "vocab", "blend.ttl")), "urn:varve-spike:g/vocab"));
        ontology.AddRange(ParseTrig(await File.ReadAllTextAsync(Path.Combine(spike, "vocab", "lib.ttl")), "urn:varve-spike:g/vocab"));
        ontology.AddRange(ParseTrig(await File.ReadAllTextAsync(Path.Combine(spike, "ontology", "spaces.trig")), "urn:varve-spike:g/spaces"));
        ontology.AddRange(ParseTrig(await File.ReadAllTextAsync(Path.Combine(spike, "ontology", "rules.trig")), Rules));
        ontology.AddRange(ParseTrig(await File.ReadAllTextAsync(Path.Combine(spike, "attempts", "attempts.trig")), "urn:varve-spike:g/attempts"));
        Dictionary<string, string> ruleFiles = new(StringComparer.Ordinal)
        {
            ["urn:dec:varve/LayersStrictlyDownward"] = "R1-layer.rq",
            ["urn:dec:spike/R2DeterminismClosure"] = "R2-determinism.rq",
            ["urn:dec:spike/R3SubstituteFromTheDeterminant"] = "R3-elaboration.rq",
            ["urn:dec:spike/R4InjectionLiftsAboveTheBan"] = "R4-injection.rq",
            ["urn:dec:spike/R5CostOnlyGrouping"] = "R5-cost-only-grouping.rq",
            ["urn:dec:spike/R6RequestKindComposes"] = "R6-request-kinds.rq",
        };
        Dictionary<string, string> ruleText = new(StringComparer.Ordinal);
        foreach ((string rule, string file) in ruleFiles)
        {
            string text = await File.ReadAllTextAsync(Path.Combine(spike, "rules", file));
            ruleText[rule] = text;
            ontology.Add(new(Iri(rule), Iri("urn:varve-spike:blend#query"), RdfTerm.Literal(Encoding.UTF8.GetBytes(text)), Iri(Rules)));
        }

        Position ontologyAt = await CommitAsync(dataset, ontology, [], "spike:ontology");
        log.Append("ontology committed at ").Append(ontologyAt).Append('\n');

        // 2. History: one Varve commit per first-parent git commit, each the
        //    net change of the projection.
        IReadOnlyList<string> commits = git.FirstParentCommitsTouching(Pathspecs);
        SortedSet<string> previous = new(StringComparer.Ordinal);
        Dictionary<long, string> gitAt = [];
        foreach (string commit in commits)
        {
            SortedSet<string> current = ProjectAt(git, commit, declarations: false);
            Position at = await CommitDeltaAsync(dataset, previous, current, "git:" + commit);
            _ = gitAt.TryAdd(at.Value, commit);
            previous = current;
        }

        log.Append(commits.Count).Append(" git commits projected, positions ").Append(ontologyAt.Value + 1).Append(" to ").Append(dataset.Head).Append('\n');

        // 3. The working tree's own state on top, with the declarations in
        //    source, which the history leaves out (reading every .cs file of
        //    every commit is the cost of a compiler-free projector).
        SortedSet<string> head = ProjectWorkingTree(repository);
        Position headAt = await CommitDeltaAsync(dataset, previous, head, "worktree");
        log.Append("working tree with declarations at ").Append(headAt).Append(", ").Append(head.Count).Append(" quads\n");
        await File.WriteAllLinesAsync(Path.Combine(output, "code-facts.nq"), head);

        // 4. Rules by level, each level one commit into the blends graph.
        foreach (string[] level in new[]
        {
            new[] { "urn:dec:varve/LayersStrictlyDownward", "urn:dec:spike/R2DeterminismClosure", "urn:dec:spike/R5CostOnlyGrouping", "urn:dec:spike/R6RequestKindComposes" },
            new[] { "urn:dec:spike/R3SubstituteFromTheDeterminant", "urn:dec:spike/R4InjectionLiftsAboveTheBan" },
        })
        {
            List<RdfQuad> derived = [];
            using (DatasetView view = dataset.Pin())
            {
                foreach (string rule in level)
                {
                    List<RdfQuad> fired = Construct(view, prefixes + ruleText[rule], Blends);
                    log.Append(rule).Append(": ").Append(fired.Count).Append(" triples\n");
                    derived.AddRange(fired);
                }
            }

            Position at = dataset.Head;
            List<RdfQuad> stamped = [.. derived];
            foreach (RdfTerm blend in derived.Where(static q => q.Predicate.Equals(Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#type")) && q.Object.Equals(Iri("urn:varve-spike:blend#Blend"))).Select(static q => q.Subject).Distinct())
            {
                stamped.Add(new(blend, Iri("urn:varve-spike:blend#atPosition"), Integer(at.Value), Iri(Blends)));
            }

            Position committed = await CommitAsync(dataset, stamped, [], "spike:rules");
            log.Append("level committed at ").Append(committed).Append(" (derived at ").Append(at).Append(")\n");
        }

        // 5. emergent, computed, into its own graph.
        string criteria = await File.ReadAllTextAsync(Path.Combine(spike, "check", "criteria.rq"));
        string emergentQuery = (await File.ReadAllTextAsync(Path.Combine(spike, "check", "emergent.rq")))
            .Replace("#CRITERIA#", criteria[..criteria.LastIndexOf("ORDER BY", StringComparison.Ordinal)], StringComparison.Ordinal);
        using (DatasetView view = dataset.Pin())
        {
            List<RdfQuad> emergent = Construct(view, prefixes + emergentQuery, Emergent);
            Position at = await CommitAsync(dataset, emergent, [], "spike:emergent");
            log.Append("emergent: ").Append(emergent.Count).Append(" constraints, at ").Append(at).Append('\n');
        }

        // 6. Reports at the head.
        using (DatasetView view = dataset.Pin())
        {
            foreach (string name in new[] { "criteria", "report", "in-force", "layer-verdicts", "references" })
            {
                string query = prefixes + await File.ReadAllTextAsync(Path.Combine(spike, "check", name + ".rq"));
                await File.WriteAllTextAsync(Path.Combine(output, name + ".tsv"), Select(view, query));
            }

            await File.WriteAllTextAsync(Path.Combine(output, "head.nq"), Dump(view));
        }

        // 7. The as-of question, end to end.
        await File.WriteAllTextAsync(Path.Combine(output, "as-of.md"), await AsOfAsync(dataset, git, gitAt, prefixes, ruleText["urn:dec:varve/LayersStrictlyDownward"]));

        await File.WriteAllTextAsync(Path.Combine(output, "run.log"), log.ToString());
        Console.Write(log);
        return 0;
    }

    /// <summary>
    /// Which decisions were in force at the commit that added
    /// Varve.Store.Azure's reference to Varve.Store, and R1's verdict on that
    /// reference derived at that position.
    /// </summary>
    private static async Task<string> AsOfAsync(Dataset dataset, GitRepository git, Dictionary<long, string> gitAt, string prefixes, string layerRule)
    {
        const string from = "urn:varve-spike:pkg:Varve.Store.Azure";
        const string to = "urn:varve-spike:pkg:Varve.Store";
        string ask = prefixes + "ASK { GRAPH <urn:varve-spike:g/code> { <" + from + "> code:references <" + to + "> } }";
        StringBuilder report = new();
        report.Append("# As of the commit that added Varve.Store.Azure -> Varve.Store\n\n");

        Position? added = null;
        foreach (long p in gitAt.Keys.Order())
        {
            using DatasetView view = await dataset.AsOfAsync(new Position(p));
            if (Ask(view, ask))
            {
                added = new Position(p);
                break;
            }
        }

        if (added is not { } position)
        {
            report.Append("The reference is in no projected position.\n");
            return report.ToString();
        }

        report.Append("- Varve position: ").Append(position).Append('\n');
        report.Append("- git commit: `").Append(git.Describe(gitAt[position.Value])).Append("`\n");

        QuadDelta delta = await dataset.DiffAsync(new Position(position.Value - 1), position);
        using (DatasetView view = await dataset.AsOfAsync(position))
        {
            report.Append("- `DiffAsync(").Append(position.Value - 1).Append(", ").Append(position).Append(")`: ")
                .Append(delta.Asserted.Length).Append(" asserted, ").Append(delta.Retracted.Length).Append(" retracted; the reference among the asserted: ");
            bool found = false;
            foreach (Quad quad in delta.Asserted)
            {
                if (view.TryExternalise(quad.Subject, out RdfTerm? s) && view.TryExternalise(quad.Object, out RdfTerm? o)
                    && s.Equals(Iri(from)) && o.Equals(Iri(to)))
                {
                    found = true;
                }
            }

            report.Append(found ? "yes" : "no").Append("\n\n");

            report.Append("## R1, derived at this position\n\n");
            List<RdfQuad> blend = Construct(view, prefixes + layerRule, Blends);
            foreach (RdfQuad quad in blend.Where(q => q.Subject.Equals(Iri("urn:varve-spike:blend/R1/" + Uri.EscapeDataString(from) + "/" + Uri.EscapeDataString(to) + "/c"))))
            {
                report.Append("- ").Append(Render(quad.Predicate)).Append(' ').Append(Render(quad.Object)).Append('\n');
            }

            report.Append("\n## Rulings in force at this position\n\nAccepted (an acceptance in the ledger graph as of the position):\n\n```\n");
            report.Append(Select(view, prefixes + """
                SELECT (COUNT(DISTINCT ?set) AS ?sets) (COUNT(DISTINCT ?d) AS ?accepted) WHERE {
                  GRAPH <urn:varve-spike:g/ledger> {
                    ?v ledger:ofDecision ?d ; ledger:set ?set .
                    ?a a ledger:Acceptance ; ledger:signsVersion ?v .
                  }
                }
                """));
            report.Append("```\n\nFiled and not accepted at this position, by set:\n\n```\n");
            report.Append(Select(view, prefixes + """
                SELECT ?set (COUNT(DISTINCT ?d) AS ?unaccepted) WHERE {
                  GRAPH <urn:varve-spike:g/ledger> {
                    ?v a ledger:DecisionVersion ; ledger:ofDecision ?d ; ledger:set ?set .
                    FILTER NOT EXISTS { ?a a ledger:Acceptance ; ledger:signsVersion ?v }
                  }
                } GROUP BY ?set ORDER BY ?set
                """));
            report.Append("```\n\nThe rulings this reference rests on, and whether each was accepted here:\n\n```\n");
            report.Append(Select(view, prefixes + """
                SELECT ?d ?statement ?accepted WHERE {
                  VALUES ?d { <urn:dec:varve/LayersStrictlyDownward> <urn:dec:varve/SameLayerReferenceIsViolation>
                              <urn:dec:varve/LayerTable> <urn:dec:varve/ProvidersAreTheirOwnPackages>
                              <urn:dec:varve/BackendContractsNameTheStore> <urn:dec:varve/FiveOperations> }
                  OPTIONAL { GRAPH <urn:varve-spike:g/ledger> { ?v ledger:ofDecision ?d ; ledger:statement ?statement } }
                  BIND(EXISTS { GRAPH <urn:varve-spike:g/ledger> { ?a ledger:signsVersion ?v } } AS ?accepted)
                }
                """));
            report.Append("```\n");
        }

        Position accepted = default;
        foreach (long p in gitAt.Keys.Order().Where(p => p > position.Value))
        {
            using DatasetView view = await dataset.AsOfAsync(new Position(p));
            if (Ask(view, prefixes + "ASK { GRAPH <urn:varve-spike:g/ledger> { ?v ledger:ofDecision <urn:dec:varve/ProvidersAreTheirOwnPackages> . ?a ledger:signsVersion ?v } }"))
            {
                accepted = new Position(p);
                break;
            }
        }

        if (accepted.Value > 0)
        {
            report.Append("\n`ProvidersAreTheirOwnPackages` is first accepted at position ").Append(accepted)
                .Append(", git `").Append(git.Describe(gitAt[accepted.Value])).Append("`.\n");
        }

        return report.ToString();
    }

    private static SortedSet<string> ProjectAt(GitRepository git, string commit, bool declarations)
    {
        List<string> paths = [.. git.ListFiles(commit).Where(p => Projector.Reads(p, declarations))];
        return Projector.Project(git.ReadFiles(commit, paths));
    }

    private static SortedSet<string> ProjectWorkingTree(string repository)
    {
        Dictionary<string, string> files = new(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(repository, "*", SearchOption.AllDirectories))
        {
            string path = Path.GetRelativePath(repository, file).Replace('\\', '/');
            if (path.StartsWith(".git/", StringComparison.Ordinal) || path.Contains("/bin/", StringComparison.Ordinal) || path.Contains("/obj/", StringComparison.Ordinal))
            {
                continue;
            }

            if (Projector.Reads(path, declarations: true))
            {
                files[path] = File.ReadAllText(file);
            }
        }

        return Projector.Project(files);
    }

    private static async Task<Position> CommitDeltaAsync(Dataset dataset, SortedSet<string> before, SortedSet<string> after, string cause)
    {
        List<string> asserted = [.. after.Where(q => !before.Contains(q))];
        List<string> retracted = [.. before.Where(q => !after.Contains(q))];
        return await CommitAsync(dataset, ParseNQuads(asserted), ParseNQuads(retracted), cause);
    }

    private static async Task<Position> CommitAsync(Dataset dataset, List<RdfQuad> asserted, List<RdfQuad> retracted, string cause)
    {
        CommitRequest request = new()
        {
            Metadata = new CommitMetadata { Cause = RdfTerm.Literal(Encoding.UTF8.GetBytes(cause)) },
        };
        foreach (RdfQuad quad in retracted)
        {
            _ = request.Retract(quad.Subject, quad.Predicate, quad.Object, quad.Graph);
        }

        foreach (RdfQuad quad in asserted)
        {
            _ = request.Assert(quad.Subject, quad.Predicate, quad.Object, quad.Graph);
        }

        CommitResult result = await dataset.CommitAsync(request);
        return result.Outcome switch
        {
            CommitOutcome.Committed or CommitOutcome.NoChange => result.Position,
            _ => throw new InvalidOperationException(cause + ": " + result + " " + result.Reason),
        };
    }

    private static List<RdfQuad> ParseTrig(string text, string defaultGraph)
    {
        List<RdfQuad> quads = [];
        RdfTerm graph = Iri(defaultGraph);
        ParseResult result = TurtleParser.Parse(Encoding.UTF8.GetBytes(text), (in QuadView q) =>
            quads.Add(new(q.Subject.Materialise(), q.Predicate.Materialise(), q.Object.Materialise(), q.HasGraph ? q.Graph.Materialise() : graph)),
            new TurtleOptions { Syntax = RdfSyntax.TriG });
        return result.ErrorCount == 0 ? quads : throw new InvalidOperationException(defaultGraph + ": " + result.FirstError);
    }

    private static List<RdfQuad> ParseNQuads(List<string> lines)
    {
        List<RdfQuad> quads = [];
        if (lines.Count == 0)
        {
            return quads;
        }

        ParseResult result = NQuadsParser.Parse(Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n"), (in QuadView q) =>
            quads.Add(new(q.Subject.Materialise(), q.Predicate.Materialise(), q.Object.Materialise(), q.Graph.Materialise())),
            new ParseOptions { Syntax = RdfSyntax.NQuads });
        return result.ErrorCount == 0 ? quads : throw new InvalidOperationException("projection: " + result.FirstError);
    }

    private static List<RdfQuad> Construct(IQuadSource source, string text, string graph)
    {
        Query query = SparqlParser.ParseQuery(text);
        using QueryResults results = Evaluator.Evaluate(query, source);
        TripleResults triples = (TripleResults)results;
        List<RdfQuad> quads = [];
        RdfTerm g = Iri(graph);
        while (triples.MoveNext())
        {
            quads.Add(new(triples.Subject, triples.Predicate, triples.Object, g));
        }

        return quads;
    }

    private static bool Ask(IQuadSource source, string text)
    {
        using QueryResults results = Evaluator.Evaluate(SparqlParser.ParseQuery(text), source);
        return ((BooleanResult)results).Value;
    }

    private static string Select(IQuadSource source, string text)
    {
        using QueryResults results = Evaluator.Evaluate(SparqlParser.ParseQuery(text), source);
        SolutionResults rows = (SolutionResults)results;
        StringBuilder table = new();
        _ = table.AppendJoin('\t', rows.Variables.Select(static v => v.ToString())).Append('\n');
        while (rows.MoveNext())
        {
            for (int i = 0; i < rows.Variables.Count; i++)
            {
                if (i > 0)
                {
                    _ = table.Append('\t');
                }

                if (rows.TryGetTerm(new ColumnIndex(i), out RdfTerm? term) && term is not null)
                {
                    _ = table.Append(Render(term));
                }
            }

            _ = table.Append('\n');
        }

        return table.ToString();
    }

    private static string Dump(DatasetView view) =>
        Select(view, "SELECT ?s ?p ?o ?g WHERE { GRAPH ?g { ?s ?p ?o } }");

    private static string Render(RdfTerm term)
    {
        ArrayBufferWriter<byte> buffer = new();
        Span<byte> span = buffer.GetSpan(1 << 16);
        return NQuadsWriter.TryWriteTerm(term, span, out int written, new WriteOptions { Syntax = RdfSyntax.NQuads })
            ? Encoding.UTF8.GetString(span[..written])
            : "?";
    }

    private static RdfTerm Iri(string iri) => RdfTerm.Iri(Encoding.UTF8.GetBytes(iri));

    private static RdfTerm Integer(long value) =>
        RdfTerm.Literal(Encoding.UTF8.GetBytes(value.ToString(CultureInfo.InvariantCulture)), Iri("http://www.w3.org/2001/XMLSchema#integer"));

    /// <summary>A quad of terms, before the store gives it handles.</summary>
    private readonly record struct RdfQuad(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object, RdfTerm Graph);
}
