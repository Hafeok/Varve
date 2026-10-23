// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CsCheck;
using Varve.Management.Model;
using Varve.Rdf;
using Varve.Shacl;
using Varve.Shacl.Model;
using Xunit;

namespace Varve.Management.Tests;

/// <summary>
/// ADR 0175's agreement property for the management vocabulary, its shapes
/// (<c>shapes/management.ttl</c>) against the code validator: over generated
/// dataset graphs, valid and with one fault each, a graph the code reads with
/// no finding conforms to the shapes; a fault the shapes state makes both
/// refuse, the shapes naming the node the code names; and a fault only the
/// code can see (ADR 0175 point 3) makes the code refuse while the shapes
/// conform, which pins the boundary.
/// </summary>
public sealed class ShapesAgreementTests
{
    /// <summary>The number of generated cases each run checks, as reported in the pull request.</summary>
    internal static readonly int Iterations = Environment.GetEnvironmentVariable("VARVE_LONG_PROPERTIES") is null ? 1_000 : 10_000;

    private const string Graph = "https://w3id.org/varve/ns#manage/dataset/d";

    private static readonly ShapesGraph Shapes = LoadShapes();

    /// <summary>Faults the shapes state: both refuse.</summary>
    private enum Seen
    {
        DatasetNameMissing,
        DatasetNameTwice,
        DatasetNameTagged,
        StorageInvalid,
        AccessScopeInvalid,
        HandoverNotBoolean,
        ClosedTwice,
        PlacedOnIri,
        GrantNoGrantee,
        GrantTwoGrantees,
        GranteeLiteral,
        GrantNoPermission,
        GrantBadPermission,
        GrantGraphLiteral,
        ReadGrantNamesNoGraph,
        AdminGrantNamesGraphs,
        AllGraphsBesideAGraph,
        GraphPrefixEmpty,
        GraphTemplateUndeclared,
        RoleWithoutClaim,
        RoleAndGroup,
        GroupClaimTypeIri,
        TemplateWithoutMatch,
        TemplateMatchInvalid,
        TemplateWithoutTemplate,
        BindingWithoutBoundGraph,
        BindingTwoBoundGraphs,
        BindingWithoutMode,
        BindingModeInvalid,
        BindingWithTemplate,
        BindingValidatesItsShapes,
        BindingNamesNoGraph,
        BindingFlagNotBoolean,
    }

    /// <summary>Faults only the code sees (ADR 0175 point 3): the code refuses, the shapes conform.</summary>
    private enum Unseen
    {
        BlankNode,
        UnknownTerm,
        EventTerm,
        ReservedTerm,
        NameNotTheGraphs,
        TwoDatasets,
        OperatorInADatasetGraph,
        TemplateSyntax,
    }

    private sealed record Declaration(bool Storage, bool Scope, bool Placed, int Roles, int Groups, bool Template, int ReadGrants, bool Admin, int Bindings);

    private static readonly Gen<Declaration> Declarations =
        from storage in Gen.Bool
        from scope in Gen.Bool
        from placed in Gen.Bool
        from roles in Gen.Int[0, 2]
        from groups in Gen.Int[0, 2]
        from template in Gen.Bool
        from reads in Gen.Int[1, 3]
        from admin in Gen.Bool
        from bindings in Gen.Int[0, 2]
        select new Declaration(storage, scope, placed, roles, groups, template, reads, admin, bindings);

    [Fact]
    public void A_valid_graph_conforms_and_the_code_finds_nothing()
    {
        Declarations.Sample(declaration =>
        {
            string body = Body(declaration, null);
            Assert.Empty(Code(body));
            Assert.Empty(ShapeResults(body));
        }, iter: Iterations);
    }

    [Fact]
    public void A_fault_the_shapes_state_is_refused_by_both_naming_the_same_node()
    {
        Gen.Select(Declarations, Gen.Enum<Seen>()).Sample((declaration, fault) =>
        {
            string body = Body(declaration with { Roles = Math.Max(1, declaration.Roles), Groups = Math.Max(1, declaration.Groups), Template = true, Bindings = Math.Max(1, declaration.Bindings) }, fault.ToString());
            List<DeclarationFinding> code = Code(body);
            List<ShaclResult> shapes = ShapeResults(body);
            Assert.True(code.Count > 0, fault + ": the code found nothing in\n" + body);
            Assert.True(shapes.Count > 0, fault + ": the shapes conform in\n" + body);
            HashSet<RdfTerm> named = new(shapes.SelectMany(r => r.Value is { } v ? (RdfTerm[])[r.FocusNode, v] : [r.FocusNode]), RdfTerm.Comparer);
            foreach (DeclarationFinding finding in code)
            {
                if (finding.Focus is { } focus)
                {
                    Assert.True(named.Contains(focus), fault + ": the code names " + Show(focus) + ", the shapes " + string.Join(", ", named.Select(Show)) + "\n" + body);
                }
            }
        }, iter: Iterations);
    }

    [Fact]
    public void A_fault_only_the_code_sees_leaves_the_shapes_conforming()
    {
        Gen.Select(Declarations, Gen.Enum<Unseen>()).Sample((declaration, fault) =>
        {
            string body = Body(declaration with { Template = true }, fault.ToString());
            Assert.True(Code(body).Count > 0, fault + ": the code found nothing in\n" + body);
            Assert.Empty(ShapeResults(body));
        }, iter: Iterations);
    }

    /// <summary>A dataset graph's body, with the named fault applied.</summary>
    private static string Body(Declaration d, string? fault)
    {
        StringBuilder text = new();
        string name = fault switch
        {
            nameof(Seen.DatasetNameMissing) => string.Empty,
            nameof(Seen.DatasetNameTwice) => " ; varve:datasetName \"d\" , \"e\"",
            nameof(Seen.DatasetNameTagged) => " ; varve:datasetName \"d\"@en",
            nameof(Unseen.NameNotTheGraphs) => " ; varve:datasetName \"other\"",
            _ => " ; varve:datasetName \"d\"",
        };
        text.Append("n:d a varve:Dataset").Append(name);
        text.Append(fault == nameof(Seen.StorageInvalid) ? " ; varve:storage varve:read" : d.Storage ? " ; varve:storage varve:File" : string.Empty);
        text.Append(fault == nameof(Seen.AccessScopeInvalid) ? " ; varve:defaultAccessScope varve:File" : d.Scope ? " ; varve:defaultAccessScope varve:Current" : string.Empty);
        text.Append(fault == nameof(Seen.PlacedOnIri) ? " ; varve:placedOn <https://x/host>" : d.Placed ? " ; varve:placedOn \"varve-1\"" : string.Empty);
        text.Append(fault == nameof(Seen.HandoverNotBoolean) ? " ; varve:automaticHandover \"yes\"" : " ; varve:automaticHandover false");
        text.Append(fault == nameof(Seen.ClosedTwice) ? " ; varve:closed true , false" : string.Empty);
        text.Append(fault == nameof(Unseen.UnknownTerm) ? " ; varve:colour \"red\"" : string.Empty);
        text.Append(fault == nameof(Unseen.ReservedTerm) ? " ; varve:erasureMode true" : string.Empty);
        text.Append(" .\n");
        if (fault == nameof(Unseen.TwoDatasets))
        {
            text.Append("n:d2 a varve:Dataset ; varve:datasetName \"d\" .\n");
        }

        for (int i = 0; i < d.Roles; i++)
        {
            text.Append("n:role").Append(i).Append(" a varve:Role");
            text.Append(i == 0 && fault == nameof(Seen.RoleWithoutClaim) ? string.Empty : " ; varve:claimValue \"R" + i + "\"");
            text.Append(i == 0 && fault == nameof(Seen.RoleAndGroup) ? " , varve:Group" : string.Empty);
            text.Append(" .\n");
        }

        for (int i = 0; i < d.Groups; i++)
        {
            text.Append("n:group").Append(i).Append(" a varve:Group ; varve:claimValue \"G").Append(i).Append('"');
            text.Append(i == 0 && fault == nameof(Seen.GroupClaimTypeIri) ? " ; varve:claimType <https://x/groups>" : " ; varve:claimType \"groups\"");
            text.Append(" .\n");
        }

        if (d.Template)
        {
            string template = fault == nameof(Unseen.TemplateSyntax) ? "https://x/{user}/" : "https://x/{sub}/";
            text.Append("n:tpl a varve:GraphTemplate");
            text.Append(fault == nameof(Seen.TemplateWithoutTemplate) ? string.Empty : " ; varve:template \"" + template + "\"");
            text.Append(fault switch
            {
                nameof(Seen.TemplateWithoutMatch) => string.Empty,
                nameof(Seen.TemplateMatchInvalid) => " ; varve:match varve:read",
                _ => " ; varve:match varve:Prefix",
            });
            text.Append(" .\n");
        }

        // Every principal and the template is named by a grant, as the code reads them.
        for (int i = 0; i < d.Roles; i++)
        {
            text.Append("n:by-role").Append(i).Append(" a varve:Grant ; varve:grantee n:role").Append(i).Append(" ; varve:permission varve:read ; varve:graphPrefix \"https://x/r").Append(i).Append("/\" .\n");
        }

        for (int i = 0; i < d.Groups; i++)
        {
            text.Append("n:by-group").Append(i).Append(" a varve:Grant ; varve:grantee n:group").Append(i).Append(" ; varve:permission varve:read ; varve:graphPrefix \"https://x/q").Append(i).Append("/\" .\n");
        }

        if (d.Template)
        {
            text.Append("n:by-template a varve:Grant ; varve:grantee <https://login.example/v2.0#carol> ; varve:permission varve:write ; varve:graphTemplate n:tpl .\n");
        }

        string grantee = d.Roles > 0 ? "n:role0" : d.Groups > 0 ? "n:group0" : "<https://login.example/v2.0#alice>";
        for (int i = 0; i < d.ReadGrants; i++)
        {
            bool first = i == 0;
            text.Append("n:read").Append(i).Append(" a varve:Grant");
            text.Append(first && fault == nameof(Seen.GrantNoGrantee) ? string.Empty
                : first && fault == nameof(Seen.GrantTwoGrantees) ? " ; varve:grantee " + grantee + " , <https://login.example/v2.0#bob>"
                : first && fault == nameof(Seen.GranteeLiteral) ? " ; varve:grantee \"alice\""
                : " ; varve:grantee " + grantee);
            text.Append(first && fault == nameof(Seen.GrantNoPermission) ? string.Empty
                : first && fault == nameof(Seen.GrantBadPermission) ? " ; varve:permission varve:File"
                : i % 2 == 0 ? " ; varve:permission varve:read" : " ; varve:permission varve:write");
            text.Append(first ? fault switch
            {
                nameof(Seen.GrantGraphLiteral) => " ; varve:graph \"not an iri\"",
                nameof(Seen.ReadGrantNamesNoGraph) => string.Empty,
                nameof(Seen.AllGraphsBesideAGraph) => " ; varve:allGraphs true ; varve:graph <https://x/g>",
                nameof(Seen.GraphPrefixEmpty) => " ; varve:graphPrefix \"\"",
                nameof(Seen.GraphTemplateUndeclared) => " ; varve:graphTemplate n:nothing",
                _ => " ; varve:graph <https://x/g" + i + "> , varve:defaultGraph",
            } : i == 1 && d.Template ? " ; varve:graphTemplate n:tpl" : " ; varve:graphPrefix \"https://x/p" + i + "/\"");
            text.Append(" .\n");
        }

        if (d.Admin || fault is nameof(Seen.AdminGrantNamesGraphs) or nameof(Unseen.OperatorInADatasetGraph))
        {
            text.Append("n:admin a varve:Grant ; varve:grantee <https://login.example/v2.0#owner>");
            text.Append(fault == nameof(Unseen.OperatorInADatasetGraph) ? " ; varve:permission varve:operator" : " ; varve:permission varve:admin");
            text.Append(fault == nameof(Seen.AdminGrantNamesGraphs) ? " ; varve:allGraphs true" : string.Empty);
            text.Append(" .\n");
        }

        for (int i = 0; i < d.Bindings; i++)
        {
            bool first = i == 0;
            text.Append("n:binding").Append(i).Append(" a varve:ShapesBinding");
            text.Append(first && fault == nameof(Seen.BindingWithoutBoundGraph) ? string.Empty
                : first && fault == nameof(Seen.BindingTwoBoundGraphs) ? " ; varve:boundGraph <https://x/shapes" + i + "> , <https://x/other>"
                : " ; varve:boundGraph <https://x/shapes" + i + ">");
            text.Append(first && fault == nameof(Seen.BindingWithoutMode) ? string.Empty
                : first && fault == nameof(Seen.BindingModeInvalid) ? " ; varve:validationMode varve:read"
                : i % 2 == 0 ? " ; varve:validationMode varve:Reject" : " ; varve:validationMode varve:Report");
            text.Append(first ? fault switch
            {
                nameof(Seen.BindingWithTemplate) => " ; varve:graphTemplate n:tpl",
                nameof(Seen.BindingValidatesItsShapes) => " ; varve:graph <https://x/shapes" + i + ">",
                nameof(Seen.BindingNamesNoGraph) => string.Empty,
                _ => " ; varve:graphPrefix \"https://x/data/\"",
            } : " ; varve:allGraphs true");
            text.Append(first && fault == nameof(Seen.BindingFlagNotBoolean) ? " ; varve:allowFullEvaluation \"yes\"" : first ? " ; varve:subClassOfInShapesGraph true" : string.Empty);
            text.Append(" .\n");
        }

        if (fault == nameof(Unseen.BlankNode))
        {
            text.Append("n:d n:note [ n:text \"a blank node\" ] .\n");
        }

        if (fault == nameof(Unseen.EventTerm))
        {
            text.Append("n:t a varve:EventType .\n");
        }

        return text.ToString();
    }

    private static List<DeclarationFinding> Code(string body) => [.. T.Read("<" + Graph + "> {\n" + body + "}").Findings];

    private static List<ShaclResult> ShapeResults(string body) => [.. Validate(Shapes, TrigDataset("<" + Graph + "> {\n" + body + "}", wrap: false), Graph).Results];

    private static ValidationReport Validate(ShapesGraph shapes, InMemoryDataset data, string graph)
    {
        GraphPattern pattern = data.TryInternalise(T.Iri(graph), out TermHandle handle) ? GraphPattern.Named(handle) : GraphPattern.DefaultGraph;
        ValidationOutcome outcome = ShaclValidator.Validate(shapes, data, pattern, null, TestContext.Current.CancellationToken);
        return Assert.IsType<ValidationReport>(outcome.Report);
    }

    private static InMemoryDataset TrigDataset(string text, bool wrap) => T.Trig(wrap ? "<" + Graph + "> { " + text + " }" : text);

    private static ShapesGraph LoadShapes()
    {
        string path = Path.Combine(Root(), "src", "Varve.Management", "shapes", "management.ttl");
        InMemoryDataset dataset = T.Trig("@prefix sh: <http://www.w3.org/ns/shacl#> .\n" + StripPrefixes(File.ReadAllText(path)));
        ShapesLoad load = ShapesGraph.Load(dataset, GraphPattern.DefaultGraph, null, TestContext.Current.CancellationToken);
        Assert.True(load.Shapes is not null, string.Join("\n", load.Problems.Select(p => p.ToString())));
        return load.Shapes!;
    }

    /// <summary>A Turtle text without its own <c>varve:</c> and <c>xsd:</c> prefix lines, which <see cref="T.Prefixes"/> declares.</summary>
    private static string StripPrefixes(string turtle) =>
        string.Join("\n", turtle.Split('\n').Where(l => !l.StartsWith("@prefix varve:", StringComparison.Ordinal) && !l.StartsWith("@prefix xsd:", StringComparison.Ordinal) && !l.StartsWith("@prefix sh:", StringComparison.Ordinal)));

    private static string Show(RdfTerm term) => term.Kind + ":" + Encoding.UTF8.GetString(term.Lexical);

    private static string Root()
    {
        DirectoryInfo? d = new(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Varve.slnx")))
        {
            d = d.Parent;
        }

        return d?.FullName ?? throw new InvalidOperationException("The repository root is not above " + AppContext.BaseDirectory + ".");
    }
}
