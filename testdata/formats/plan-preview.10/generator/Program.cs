// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Writes a `varve apply` saved plan, format 1, as v0.1.0-preview.10 writes
// it, from that release's published Varve.Management package (docs/formats/,
// ADR 0216). A plan is versioned and positional, not read forever: a later
// release reads format 1 while it can and refuses an unknown format, telling
// the operator to plan again. Beside it, expectations/plan.txt: what the
// release's own reader gives back, graph by graph.
using System.Text;
using Varve.Management;
using Varve.Management.Model;
using Varve.Rdf;

string root = args[0];
static RdfTerm Iri(string iri) => RdfTerm.Iri(Encoding.UTF8.GetBytes(iri));
ManagementPlan plan = new(new ManagementPosition(42),
[
    new GraphPlan(Iri("https://varve.example.org/management/datasets"), new ManagementPosition(40),
        ["<https://varve.example.org/datasets/orders> <https://w3id.org/varve#storage> \"file\" ."],
        ["<https://varve.example.org/datasets/orders> <https://w3id.org/varve#storage> \"file\" ."],
        ["<https://varve.example.org/datasets/orders> <https://w3id.org/varve#storage> \"memory\" ."]),
    new GraphPlan(Iri("https://varve.example.org/management/grants"), new ManagementPosition(0),
        ["<https://varve.example.org/grants/1> <https://w3id.org/varve#role> \"reader\" .", "<https://varve.example.org/grants/1> <https://w3id.org/varve#agent> <https://issuer.example/#alice> ."],
        ["<https://varve.example.org/grants/1> <https://w3id.org/varve#agent> <https://issuer.example/#alice> .", "<https://varve.example.org/grants/1> <https://w3id.org/varve#role> \"reader\" ."],
        []),
]);
byte[] json = plan.ToJson();
ManagementPlan back = ManagementPlan.FromJson(json);
StringBuilder expected = new();
expected.Append("position ").Append(back.Position.Value).Append('\n');

foreach (GraphPlan graph in back.Graphs)
{
    expected.Append("graph ").Append(Encoding.UTF8.GetString(graph.Graph.Lexical)).Append(" version ").Append(graph.Version.Value).Append('\n');

    foreach ((string name, IReadOnlyList<string> lines) in new[] { ("content", graph.Content), ("added", graph.Added), ("removed", graph.Removed) })
    {
        foreach (string line in lines)
        {
            expected.Append("  ").Append(name).Append(' ').Append(line).Append('\n');
        }
    }
}

Directory.CreateDirectory(Path.Combine(root, "expectations"));
File.WriteAllBytes(Path.Combine(root, "format-1.plan.json"), json);
File.WriteAllText(Path.Combine(root, "expectations", "plan.txt"), expected.ToString());
Console.WriteLine(json.Length + " bytes");
