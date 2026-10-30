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
/// ADR 0175's agreement properties for the management vocabulary: its shapes
/// (<c>shapes/management.ttl</c>) against the code validator frozen as the
/// oracle (<see cref="Oracle.CodeDeclarationReader"/>), and the runtime against
/// the oracle: over generated
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
        TBoxWithoutBoundGraph,
        TBoxBoundGraphDefault,
        TBoxBoundGraphLiteral,
        TBoxWithoutProfile,
        TBoxTwoProfiles,
        TBoxProfileInvalid,
        TBoxNamesNoGraph,
        TBoxWithTemplate,
        TBoxDataNamesItsTBox,
        TBoxDefaultTwice,
        TBoxDefaultReserved,
        EventDeclarationsTwice,
        EventDeclarationsInvalid,
        EventDeclarationsOffTheDataset,
        MovedTypeWithoutSourceGraph,
        MovedTypeTwoSourceGraphs,
        MovedTypeSourceGraphLiteral,
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
        TBoxProfileReserved,
        TBoxReservedTerm,
        TBoxBindingsOverlap,
        TBoxDefaultsDiffer,
    }

    /// <summary>Where the dataset's event declarations are (ADR 0167): 0 not stated, then varve:InDataset (a cancel), varve:Moving, varve:Moved.</summary>
    private sealed record Declaration(bool Storage, bool Scope, bool Placed, int Roles, int Groups, bool Template, int ReadGrants, bool Admin, int Bindings, int Events, int TBoxBindings);

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
        from events in Gen.Int[0, 3]
        from tboxBindings in Gen.Int[0, 2]
        select new Declaration(storage, scope, placed, roles, groups, template, reads, admin, bindings, events, tboxBindings);

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
            string body = Body(declaration with { Roles = Math.Max(1, declaration.Roles), Groups = Math.Max(1, declaration.Groups), Template = true, Bindings = Math.Max(1, declaration.Bindings), TBoxBindings = Math.Max(1, declaration.TBoxBindings) }, fault.ToString());
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
            string body = Body(declaration with { Template = true, TBoxBindings = Math.Max(1, declaration.TBoxBindings) }, fault.ToString());
            Assert.True(Code(body).Count > 0, fault + ": the code found nothing in\n" + body);
            Assert.Empty(ShapeResults(body));
        }, iter: Iterations);
    }

    /// <summary>
    /// The runtime — the shapes and the residual check — refuses exactly what
    /// the oracle refuses, over valid graphs and every fault, and every node
    /// the oracle names is one the runtime names.
    /// </summary>
    [Fact]
    public void The_runtime_refuses_exactly_what_the_oracle_refuses()
    {
        string[] faults = [.. Enum.GetNames<Seen>(), .. Enum.GetNames<Unseen>()];
        Gen.Select(Declarations, Gen.Int[-1, faults.Length - 1]).Sample((declaration, index) =>
        {
            string? fault = index < 0 ? null : faults[index];
            string body = Body(declaration with { Roles = Math.Max(1, declaration.Roles), Groups = Math.Max(1, declaration.Groups), Template = true, Bindings = Math.Max(1, declaration.Bindings), TBoxBindings = Math.Max(1, declaration.TBoxBindings) }, fault);
            List<DeclarationFinding> oracle = Code(body);
            List<DeclarationFinding> runtime = Runtime(body);
            Assert.True(oracle.Count == 0 == (runtime.Count == 0), (fault ?? "valid") + ": the oracle found " + oracle.Count + ", the runtime " + runtime.Count + "\n" + body);
            HashSet<RdfTerm> named = new(runtime.SelectMany(f => f.Focus is { } focus ? [focus] : Array.Empty<RdfTerm>()), RdfTerm.Comparer);
            ShapeResults(body).ForEach(r => { if (r.Value is { } v) { named.Add(v); } });
            foreach (DeclarationFinding finding in oracle)
            {
                if (finding.Focus is { } focus)
                {
                    Assert.True(named.Contains(focus), fault + ": the oracle names " + Show(focus) + "\n" + body);
                }
            }
        }, iter: Iterations);
    }

    /// <summary>
    /// Management plane part 2's fixture of moved declarations
    /// (<c>fixtures/moved-declarations/notes.trig</c>, ADR 0167) in its
    /// management form, as the system dataset holds it: the shapes conform and
    /// the runtime and the oracle find nothing. With each fault of the move's
    /// terms applied to it, the runtime and the oracle refuse, and the runtime
    /// names the node the oracle names. Its catalogue form is the events tests'.
    /// </summary>
    [Theory]
    [InlineData("none")]
    [InlineData("no source graph")]
    [InlineData("two source graphs")]
    [InlineData("a literal source graph")]
    [InlineData("an invalid state")]
    [InlineData("the state off the dataset")]
    [InlineData("not moved")]
    public void The_moved_fixture_agrees_in_its_management_form(string fault)
    {
        string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "moved-declarations", "notes.trig")).ReplaceLineEndings("\n");
        text = fault switch
        {
            "none" => text,
            "no source graph" => Once(text, "    varve:sourceGraph varve:defaultGraph ;\n", string.Empty),
            "two source graphs" => Once(text, "varve:sourceGraph varve:defaultGraph ;", "varve:sourceGraph varve:defaultGraph , <https://notes.example/other> ;"),
            "a literal source graph" => Once(text, "varve:sourceGraph <https://notes.example/shared> ;", "varve:sourceGraph \"https://notes.example/shared\" ;"),
            "an invalid state" => Once(text, "varve:eventDeclarations varve:Moved .", "varve:eventDeclarations varve:Off ."),
            "the state off the dataset" => Once(text, "n:users a varve:Role ;", "n:users varve:eventDeclarations varve:Moving .\n  n:users a varve:Role ;"),
            "not moved" => Once(text, "varve:eventDeclarations varve:Moved .", "varve:eventDeclarations varve:Moving ."),
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };

        InMemoryDataset data = ManagementTrig.TryParse(Encoding.UTF8.GetBytes(text), null, out string? error) ?? throw new InvalidOperationException(error);
        List<DeclarationFinding> oracle = [.. Oracle.CodeDeclarationReader.Read(data).Findings];
        List<DeclarationFinding> runtime = [.. DeclarationReader.Read(data).Findings];
        string shown = fault + "\noracle: " + string.Join("; ", oracle.Select(f => f.ToString())) + "\nruntime: " + string.Join("; ", runtime.Select(f => f.ToString()));
        const string Graph = "https://w3id.org/varve/ns#manage/dataset/notes";
        List<ShaclResult> shapes = [.. Validate(Shapes, data, Graph).Results];

        if (fault == "none")
        {
            Assert.True(oracle.Count == 0 && runtime.Count == 0 && shapes.Count == 0, shown);
            return;
        }

        Assert.True(oracle.Count > 0 && runtime.Count > 0, shown);
        HashSet<RdfTerm> named = new(runtime.SelectMany(f => f.Focus is { } focus ? [focus] : Array.Empty<RdfTerm>()), RdfTerm.Comparer);
        foreach (DeclarationFinding finding in oracle)
        {
            if (finding.Focus is { } focus)
            {
                Assert.True(named.Contains(focus), "the oracle names " + Show(focus) + "\n" + shown);
            }
        }

        // The graph's state is the code's to see; every other fault of the move's terms, the shapes'.
        Assert.True(fault == "not moved" ? shapes.Count == 0 : shapes.Count > 0, shown);
    }

    private static string Once(string text, string from, string to)
    {
        Assert.Equal(1, text.Split(from).Length - 1);
        return text.Replace(from, to, StringComparison.Ordinal);
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

        // The move's state: a moved type needs varve:Moved, an event term out of place needs any other.
        int events = fault switch
        {
            nameof(Seen.MovedTypeWithoutSourceGraph) or nameof(Seen.MovedTypeTwoSourceGraphs) or nameof(Seen.MovedTypeSourceGraphLiteral) => 3,
            nameof(Unseen.EventTerm) when d.Events == 3 => 2,
            nameof(Seen.EventDeclarationsTwice) or nameof(Seen.EventDeclarationsInvalid) => 0,
            _ => d.Events,
        };
        text.Append(fault switch
        {
            nameof(Seen.EventDeclarationsTwice) => " ; varve:eventDeclarations varve:Moving , varve:InDataset",
            nameof(Seen.EventDeclarationsInvalid) => " ; varve:eventDeclarations varve:Off",
            _ => events switch { 1 => " ; varve:eventDeclarations varve:InDataset", 2 => " ; varve:eventDeclarations varve:Moving", 3 => " ; varve:eventDeclarations varve:Moved", _ => string.Empty },
        });
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

        // TBox bindings (ADR 0194): disjoint prefixes, the first stating the dataset's default.
        for (int i = 0; i < d.TBoxBindings; i++)
        {
            bool first = i == 0;
            text.Append("n:tbox").Append(i).Append(" a varve:TBoxBinding");
            text.Append(first ? fault switch
            {
                nameof(Seen.TBoxWithoutBoundGraph) => string.Empty,
                nameof(Seen.TBoxBoundGraphDefault) => " ; varve:boundGraph varve:defaultGraph",
                nameof(Seen.TBoxBoundGraphLiteral) => " ; varve:boundGraph \"https://x/tbox0\"",
                _ => " ; varve:boundGraph <https://x/tbox0>",
            } : " ; varve:boundGraph <https://x/tbox" + i + "> , <https://x/tbox" + i + "b>");
            text.Append(first ? fault switch
            {
                nameof(Seen.TBoxWithoutProfile) => string.Empty,
                nameof(Seen.TBoxTwoProfiles) => " ; varve:profile varve:Rewriting , varve:OWL2RL",
                nameof(Seen.TBoxProfileInvalid) => " ; varve:profile varve:read",
                nameof(Unseen.TBoxProfileReserved) => " ; varve:profile varve:OWL2RL",
                _ => " ; varve:profile varve:Rewriting",
            } : " ; varve:profile varve:Rewriting");
            text.Append(first ? fault switch
            {
                nameof(Seen.TBoxNamesNoGraph) => string.Empty,
                nameof(Seen.TBoxWithTemplate) => " ; varve:graphPrefix \"https://x/t0/\" ; varve:graphTemplate n:tpl",
                nameof(Seen.TBoxDataNamesItsTBox) => " ; varve:graph <https://x/tbox0>",
                _ => " ; varve:graphPrefix \"https://x/t0/\" ; varve:graph <https://x/data0>",
            } : " ; varve:graphPrefix \"https://x/t" + i + "/\"");
            text.Append(first ? fault switch
            {
                nameof(Seen.TBoxDefaultTwice) => " ; varve:defaultEntailment <http://www.w3.org/ns/entailment/RDFS> , <http://www.w3.org/ns/entailment/D>",
                nameof(Seen.TBoxDefaultReserved) => " ; varve:defaultEntailment <http://www.w3.org/ns/entailment/OWL-RDF-Based>",
                _ => " ; varve:defaultEntailment <http://www.w3.org/ns/entailment/RDFS>",
            } : string.Empty);
            text.Append(first && fault == nameof(Unseen.TBoxReservedTerm) ? " ; varve:crossGraph true" : string.Empty);
            text.Append(" .\n");
        }

        if (fault == nameof(Unseen.TBoxBindingsOverlap))
        {
            text.Append("n:tbox-over a varve:TBoxBinding ; varve:boundGraph <https://x/tbox-over> ; varve:profile varve:Rewriting ; varve:graph <https://x/t0/g> .\n");
        }

        if (fault == nameof(Unseen.TBoxDefaultsDiffer))
        {
            text.Append("n:tbox-other a varve:TBoxBinding ; varve:boundGraph <https://x/tbox-other> ; varve:profile varve:Rewriting ; varve:graphPrefix \"https://x/tz/\" ; varve:defaultEntailment <http://www.w3.org/ns/entailment/D> .\n");
        }

        if (fault == nameof(Unseen.BlankNode))
        {
            text.Append("n:d n:note [ n:text \"a blank node\" ] .\n");
        }

        // A moved type, as the move copies it (ADR 0167): its catalogue graph's source, by varve:sourceGraph.
        if (events == 3)
        {
            text.Append("n:t a varve:EventType");
            text.Append(fault switch
            {
                nameof(Seen.MovedTypeWithoutSourceGraph) => string.Empty,
                nameof(Seen.MovedTypeTwoSourceGraphs) => " ; varve:sourceGraph varve:defaultGraph , <https://x/g>",
                nameof(Seen.MovedTypeSourceGraphLiteral) => " ; varve:sourceGraph \"https://x/g\"",
                _ => " ; varve:sourceGraph varve:defaultGraph",
            });
            text.Append(" .\n");
        }

        if (fault == nameof(Seen.EventDeclarationsOffTheDataset))
        {
            text.Append("n:read0 varve:eventDeclarations varve:Moving .\n");
        }

        // An event term in a graph that has not moved, well formed: only the code sees where it is.
        if (fault == nameof(Unseen.EventTerm))
        {
            text.Append("n:t a varve:EventType ; varve:sourceGraph varve:defaultGraph .\n");
        }

        return text.ToString();
    }

    /// <summary>The oracle's findings: the code validator as it stood before the switch.</summary>
    private static List<DeclarationFinding> Code(string body) => [.. Oracle.CodeDeclarationReader.Read(T.Trig("<" + Graph + "> {\n" + body + "}")).Findings];

    /// <summary>The runtime's findings: the shapes' and the residual check's.</summary>
    private static List<DeclarationFinding> Runtime(string body) => [.. T.Read("<" + Graph + "> {\n" + body + "}").Findings];

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
