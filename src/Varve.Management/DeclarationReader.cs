// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Iri;
using Varve.Management.Model;
using Varve.Rdf;

namespace Varve.Management;

/// <summary>
/// Reads the management graphs of a quad source into declarations, with the
/// findings that refuse them. The management shapes decide every rule SHACL
/// Core can state (ADR 0175), each finding built from a report result, and a
/// residual check here the rest (ADR 0166 point 4). A graph with a finding
/// contributes nothing the reader found it wrong about.
/// </summary>
[Contract(typeof(VarveManagementAndTheVocabulary.CodeValidatorNamesFocusAndPath), Role = "the reader and code validator of management declarations")]
public static class DeclarationReader
{
    /// <summary>Every graph of <paramref name="source"/>, read: the management graphs as declarations, any other as a finding.</summary>
    public static Declarations Read(IQuadSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Read(Graphs(source));
    }

    /// <summary>The named graphs of <paramref name="source"/> and their triples, the default graph under none.</summary>
    internal static SortedDictionary<RdfTerm, List<Triple>> Graphs(IQuadSource source)
    {
        SortedDictionary<RdfTerm, List<Triple>> graphs = new(TermOrder.Instance);
        using IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);

        while (cursor.MoveNext())
        {
            Quad quad = cursor.Current;

            if (!source.TryExternalise(quad.Subject, out RdfTerm? s)
                || !source.TryExternalise(quad.Predicate, out RdfTerm? p)
                || !source.TryExternalise(quad.Object, out RdfTerm? o))
            {
                continue;
            }

            RdfTerm graph = quad.Graph.IsNone || !source.TryExternalise(quad.Graph, out RdfTerm? named) ? DefaultGraphKey : named;

            if (!graphs.TryGetValue(graph, out List<Triple>? triples))
            {
                triples = [];
                graphs[graph] = triples;
            }

            triples.Add(new Triple(s, p, o));
        }

        return graphs;
    }

    // The default graph, as a key no IRI can take.
    internal static readonly RdfTerm DefaultGraphKey = RdfTerm.BlankNode("default-graph"u8);

    internal static Declarations Read(SortedDictionary<RdfTerm, List<Triple>> graphs)
    {
        SortedDictionary<string, DatasetDeclaration> datasets = new(StringComparer.Ordinal);
        List<GrantDeclaration> fleet = [];
        List<string> bootstrap = [];
        List<DeclarationFinding> findings = [];

        foreach ((RdfTerm graph, List<Triple> triples) in graphs)
        {
            if (graph.Equals(DefaultGraphKey))
            {
                findings.Add(new(graph, null, null, "the system dataset's default graph holds nothing: every declaration is in a management graph"));
                continue;
            }

            GraphReader reader = new(graph, triples, findings);

            if (!reader.CheckTerms())
            {
                continue;
            }

            if (graph.Equals(ManagementVocabulary.FleetGraph))
            {
                fleet.AddRange(reader.Grants(dataset: null));
                reader.RefuseDatasets("the fleet graph declares no dataset: each dataset has a graph of its own");
                findings.AddRange(ManagementShapes.Validate(graph, triples));
            }
            else if (graph.Equals(ManagementVocabulary.BootstrapGraph))
            {
                bootstrap.AddRange(reader.Bootstrap());
            }
            else if (ManagementVocabulary.TryGetDataset(graph, out string? name))
            {
                if (reader.Dataset(name) is { } declared)
                {
                    datasets[name] = declared;
                }

                findings.AddRange(ManagementShapes.Validate(graph, triples));
            }
            else
            {
                findings.Add(new(graph, null, null, "a graph of the system dataset is varve:manage/fleet, varve:manage/bootstrap or varve:manage/dataset/{name}"));
            }
        }

        bootstrap.Sort(StringComparer.Ordinal);
        return new Declarations { Datasets = datasets, FleetGrants = fleet, Bootstrap = bootstrap, Findings = findings };
    }

    internal readonly record struct Triple(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object);

    private sealed class GraphReader
    {
        private readonly RdfTerm _graph;
        private readonly List<DeclarationFinding> _findings;
        private readonly SortedDictionary<RdfTerm, List<(RdfTerm Predicate, RdfTerm Object)>> _nodes = new(TermOrder.Instance);

        public GraphReader(RdfTerm graph, List<Triple> triples, List<DeclarationFinding> findings)
        {
            _graph = graph;
            _findings = findings;
            Triples = triples;

            foreach (Triple triple in triples)
            {
                if (!_nodes.TryGetValue(triple.Subject, out List<(RdfTerm, RdfTerm)>? list))
                {
                    list = [];
                    _nodes[triple.Subject] = list;
                }

                list.Add((triple.Predicate, triple.Object));
            }

            foreach (List<(RdfTerm Predicate, RdfTerm Object)> list in _nodes.Values)
            {
                list.Sort(static (a, b) =>
                {
                    int byPredicate = TermOrder.Instance.Compare(a.Predicate, b.Predicate);
                    return byPredicate != 0 ? byPredicate : TermOrder.Instance.Compare(a.Object, b.Object);
                });
            }
        }

        private List<Triple> Triples { get; }

        // Blank nodes, event terms, reserved and unknown varve: terms.
        public bool CheckTerms()
        {
            int before = _findings.Count;

            // A dataset's graph holds event declarations once they moved there (ADR 0167).
            bool moved = ManagementVocabulary.TryGetDataset(_graph, out _)
                && Triples.Exists(static t => t.Predicate.Equals(ManagementVocabulary.EventDeclarations) && t.Object.Equals(ManagementVocabulary.Moved));

            foreach (Triple triple in Triples)
            {
                if (triple.Subject.Kind == RdfTermKind.BlankNode || triple.Object.Kind == RdfTermKind.BlankNode)
                {
                    Residual(triple.Subject.Kind == RdfTermKind.BlankNode ? null : triple.Subject, triple.Predicate,
                        "a declaration holds no blank node: give every node an IRI, so that applying it again changes nothing");
                    continue;
                }

                Check(triple.Subject, triple.Subject, null);
                Check(triple.Predicate, triple.Subject, triple.Predicate);

                if (triple.Object.Kind == RdfTermKind.Iri)
                {
                    Check(triple.Object, triple.Subject, triple.Predicate);
                }
            }

            return _findings.Count == before;

            void Check(RdfTerm term, RdfTerm focus, RdfTerm? path)
            {
                if (ManagementVocabulary.LocalName(term) is not { } local || local.StartsWith("manage/", StringComparison.Ordinal))
                {
                    return;
                }

                if (local == "bootstrapOperator" && _graph.Equals(ManagementVocabulary.BootstrapGraph))
                {
                    return;
                }

                if (ManagementVocabulary.EventTerms.Contains(local))
                {
                    // Whether the graph says its declarations moved is the graph's own state, which Core cannot state (ADR 0175).
                    if (!moved)
                    {
                        Residual(focus, path, "varve:" + local + " is an event-schema term: a dataset's event declarations are in its catalogue graphs until POST /manage/datasets/{name}/events/move moves them here, and a graph holds them only once it says varve:eventDeclarations varve:Moved (ADR 0167)");
                    }
                }
                else if (ManagementVocabulary.Reserved.TryGetValue(local, out string? reason))
                {
                    Residual(focus, path, reason);
                }
                else if (!ManagementVocabulary.Known.Contains(local))
                {
                    Residual(focus, path, "varve:" + local + " is not a term of the management vocabulary (ADR 0166)");
                }
            }
        }

        public void RefuseDatasets(string message)
        {
            foreach (RdfTerm node in Typed(ManagementVocabulary.Dataset))
            {
                Residual(node, ManagementVocabulary.Type, message);
            }
        }

        public List<string> Bootstrap()
        {
            List<string> set = [];

            foreach (Triple triple in Triples)
            {
                if (triple.Predicate.Equals(ManagementVocabulary.BootstrapOperator) && triple.Object.Kind == RdfTermKind.Literal)
                {
                    set.Add(Encoding.UTF8.GetString(triple.Object.Lexical));
                }
                else
                {
                    Residual(triple.Subject, triple.Predicate, "the bootstrap graph holds only varve:bootstrapOperator literals, written by the system writer");
                }
            }

            return set;
        }

        public DatasetDeclaration? Dataset(string name)
        {
            List<RdfTerm> declared = [.. Typed(ManagementVocabulary.Dataset)];

            if (declared.Count != 1)
            {
                Residual(null, null, "a dataset's graph declares exactly one varve:Dataset; this one declares " + declared.Count);
                return null;
            }

            RdfTerm node = declared[0];
            string? stated = Literal(node, ManagementVocabulary.DatasetName, required: true);

            if (stated is not null && stated != name)
            {
                Residual(node, ManagementVocabulary.DatasetName, "varve:datasetName is \"" + stated + "\" in the graph of dataset " + name + "; they must be equal");
            }

            StorageKind storage = One(node, ManagementVocabulary.Storage) switch
            {
                null => StorageKind.File,
                { } value when value.Equals(ManagementVocabulary.File) => StorageKind.File,
                { } value when value.Equals(ManagementVocabulary.Memory) => StorageKind.Memory,
                { } value when value.Equals(ManagementVocabulary.ObjectStorage) => StorageKind.ObjectStorage,
                _ => Invalid(StorageKind.File, node, ManagementVocabulary.Storage, "varve:storage is varve:File, varve:Memory or varve:ObjectStorage"),
            };
            HistoryScope? scope = One(node, ManagementVocabulary.DefaultAccessScope) switch
            {
                null => null,
                { } value when value.Equals(ManagementVocabulary.AllHistory) => HistoryScope.AllHistory,
                { } value when value.Equals(ManagementVocabulary.Current) => HistoryScope.Current,
                _ => Invalid<HistoryScope?>(null, node, ManagementVocabulary.DefaultAccessScope, "varve:defaultAccessScope is varve:AllHistory or varve:Current"),
            };
            string? placed = Literal(node, ManagementVocabulary.PlacedOn, required: false);
            bool handover = Boolean(node, ManagementVocabulary.AutomaticHandover) ?? false;
            bool closed = Boolean(node, ManagementVocabulary.Closed) ?? false;
            EventDeclarationState events = EventState(node);
            List<GrantDeclaration> grants = Grants(name);
            List<BindingDeclaration> bindings = [.. Bindings()];

            foreach (GrantDeclaration grant in grants)
            {
                if (grant.Access == GrantAccess.Operator)
                {
                    Residual(grant.Node, ManagementVocabulary.Permission, "varve:operator is granted in the fleet graph only");
                }
            }

            return new DatasetDeclaration
            {
                Name = name,
                Node = node,
                Storage = storage,
                PlacedOn = placed,
                DefaultAccessScope = scope,
                AutomaticHandover = handover,
                Closed = closed,
                EventDeclarations = events,
                Grants = grants.FindAll(static grant => grant.Access != GrantAccess.Operator),
                Bindings = bindings,
            };
        }

        // varve:eventDeclarations, on the dataset node only (ADR 0167).
        private EventDeclarationState EventState(RdfTerm node)
        {
            foreach (Triple triple in Triples)
            {
                if (triple.Predicate.Equals(ManagementVocabulary.EventDeclarations) && !triple.Subject.Equals(node))
                {
                    Stated(triple.Subject, ManagementVocabulary.EventDeclarations, "varve:eventDeclarations is stated on the dataset's node only");
                }
            }

            return One(node, ManagementVocabulary.EventDeclarations) switch
            {
                null => EventDeclarationState.InDataset,
                { } value when value.Equals(ManagementVocabulary.InDataset) => EventDeclarationState.InDataset,
                { } value when value.Equals(ManagementVocabulary.Moving) => EventDeclarationState.Moving,
                { } value when value.Equals(ManagementVocabulary.Moved) => EventDeclarationState.Moved,
                _ => Invalid(EventDeclarationState.InDataset, node, ManagementVocabulary.EventDeclarations, "varve:eventDeclarations is varve:InDataset, varve:Moving or varve:Moved"),
            };
        }

        public List<GrantDeclaration> Grants(string? dataset)
        {
            List<GrantDeclaration> grants = [];

            foreach (RdfTerm node in Typed(ManagementVocabulary.Grant))
            {
                List<RdfTerm> grantees = Values(node, ManagementVocabulary.Grantee);
                List<RdfTerm> permissions = Values(node, ManagementVocabulary.Permission);

                if (grantees.Count != 1)
                {
                    Stated(node, ManagementVocabulary.Grantee, "a grant names exactly one varve:grantee; this one names " + grantees.Count);
                    continue;
                }

                if (permissions.Count != 1)
                {
                    Stated(node, ManagementVocabulary.Permission, "a grant has exactly one varve:permission; this one has " + permissions.Count);
                    continue;
                }

                GrantAccess? permission = permissions[0] switch
                {
                    { } p when p.Equals(ManagementVocabulary.Read) => GrantAccess.Read,
                    { } p when p.Equals(ManagementVocabulary.Write) => GrantAccess.Write,
                    { } p when p.Equals(ManagementVocabulary.Admin) => GrantAccess.Admin,
                    { } p when p.Equals(ManagementVocabulary.Operator) => GrantAccess.Operator,
                    _ => null,
                };

                if (permission is not { } granted)
                {
                    Stated(node, ManagementVocabulary.Permission, "varve:permission is varve:read, varve:write, varve:admin or varve:operator");
                    continue;
                }

                if (dataset is null && granted != GrantAccess.Operator)
                {
                    Residual(node, ManagementVocabulary.Permission, "the fleet graph grants varve:operator only; a dataset's grants are in its own graph");
                    continue;
                }

                if (PrincipalOf(grantees[0]) is not { } principal)
                {
                    continue;
                }

                GraphSet? graphs = Graphs(node, wide: granted is GrantAccess.Admin or GrantAccess.Operator);

                if (graphs is null)
                {
                    continue;
                }

                grants.Add(new GrantDeclaration(node, dataset, principal, granted, graphs));
            }

            return grants;
        }

        private IEnumerable<BindingDeclaration> Bindings()
        {
            foreach ((RdfTerm type, BindingKind kind) in new[] { (ManagementVocabulary.ShapesBinding, BindingKind.Shapes), (ManagementVocabulary.TBoxBinding, BindingKind.TBox) })
            {
                foreach (RdfTerm node in Typed(type))
                {
                    List<RdfTerm> bound = Values(node, ManagementVocabulary.BoundGraph);

                    if (bound.Count != 1 || bound[0].Kind != RdfTermKind.Iri || !IsAbsolute(bound[0]))
                    {
                        Stated(node, ManagementVocabulary.BoundGraph, "a binding names exactly one varve:boundGraph, an absolute IRI");
                        continue;
                    }

                    if (kind == BindingKind.TBox)
                    {
                        yield return new BindingDeclaration(node, kind, bound[0]);
                        continue;
                    }

                    if (ShapesBinding(node, bound[0]) is { } shapes)
                    {
                        yield return shapes;
                    }
                }
            }
        }

        /// <summary>A shapes binding's graphs, mode and flags (ADR 0170); null after a finding.</summary>
        private BindingDeclaration? ShapesBinding(RdfTerm node, RdfTerm shapesGraph)
        {
            List<RdfTerm> modes = Values(node, ManagementVocabulary.ValidationMode);
            ShapesMode? mode = modes.Count != 1 ? null : modes[0] switch
            {
                { } m when m.Equals(ManagementVocabulary.Reject) => ShapesMode.Reject,
                { } m when m.Equals(ManagementVocabulary.Report) => ShapesMode.Report,
                { } m when m.Equals(ManagementVocabulary.Off) => ShapesMode.Off,
                _ => null,
            };

            if (mode is not { } validationMode)
            {
                Stated(node, ManagementVocabulary.ValidationMode, "a shapes binding has exactly one varve:validationMode: varve:Reject, varve:Report or varve:Off");
                return null;
            }

            if (Values(node, ManagementVocabulary.GraphTemplateProperty).Count > 0)
            {
                Stated(node, ManagementVocabulary.GraphTemplateProperty, "a shapes binding names its graphs without varve:graphTemplate: a template expands per caller, and a binding has none");
                return null;
            }

            if (Graphs(node, wide: false, "a shapes binding") is not { } graphs)
            {
                return null;
            }

            foreach (RdfTerm graph in graphs.Graphs)
            {
                if (graph.Equals(shapesGraph))
                {
                    Stated(node, ManagementVocabulary.Graph, "a shapes binding's graphs do not name its own shapes graph, which is never data (ADR 0170)");
                    return null;
                }
            }

            return new BindingDeclaration(node, BindingKind.Shapes, shapesGraph)
            {
                Validates = graphs,
                Mode = validationMode,
                AllowFullEvaluation = Boolean(node, ManagementVocabulary.AllowFullEvaluation) ?? false,
                SubClassOfInShapesGraph = Boolean(node, ManagementVocabulary.SubClassOfInShapesGraph) ?? false,
            };
        }

        private Principal? PrincipalOf(RdfTerm node)
        {
            if (node.Kind != RdfTermKind.Iri)
            {
                Stated(node, ManagementVocabulary.Grantee, "a grantee is an IRI: an agent's, or a role's or group's node");
                return null;
            }

            bool role = Has(node, ManagementVocabulary.Type, ManagementVocabulary.Role);
            bool group = Has(node, ManagementVocabulary.Type, ManagementVocabulary.Group);

            if (role && group)
            {
                Stated(node, ManagementVocabulary.Type, "a principal is a role or a group, not both");
                return null;
            }

            if (role || group)
            {
                string? value = Literal(node, ManagementVocabulary.ClaimValue, required: true);

                if (value is null)
                {
                    return null;
                }

                if (role)
                {
                    return Model.Principal.Role(value);
                }

                return Model.Principal.Group(Literal(node, ManagementVocabulary.ClaimType, required: false) ?? "groups", value);
            }

            if (!IsAbsolute(node))
            {
                Stated(node, ManagementVocabulary.Grantee, "an agent is named by its absolute IRI, the token's issuer, a hash and its subject");
                return null;
            }

            return Model.Principal.Agent(Encoding.UTF8.GetString(node.Lexical));
        }

        private GraphSet? Graphs(RdfTerm node, bool wide, string owner = "a read or write grant")
        {
            bool all = Boolean(node, ManagementVocabulary.AllGraphs) ?? false;
            List<RdfTerm> graphs = Values(node, ManagementVocabulary.Graph);
            List<RdfTerm> prefixes = Values(node, ManagementVocabulary.GraphPrefix);
            List<RdfTerm> templates = Values(node, ManagementVocabulary.GraphTemplateProperty);
            bool named = graphs.Count > 0 || prefixes.Count > 0 || templates.Count > 0;

            if (wide)
            {
                if (all || named)
                {
                    Stated(node, ManagementVocabulary.Permission, "admin and operator cover every graph: such a grant names no graph set (ADR 0107)");
                    return null;
                }

                return GraphSet.Everything;
            }

            if (all && named)
            {
                Stated(node, ManagementVocabulary.AllGraphs, "varve:allGraphs covers every graph; name no other graph beside it");
                return null;
            }

            if (all)
            {
                return GraphSet.Everything;
            }

            if (!named)
            {
                Stated(node, null, owner + " names its graphs: varve:allGraphs true, varve:graph, varve:graphPrefix or varve:graphTemplate");
                return null;
            }

            bool includesDefault = false;
            List<RdfTerm> explicitGraphs = [];
            List<string> prefixValues = [];
            List<GraphTemplate> templateValues = [];
            bool ok = true;

            foreach (RdfTerm graph in graphs)
            {
                if (graph.Equals(ManagementVocabulary.DefaultGraph))
                {
                    includesDefault = true;
                }
                else if (graph.Kind == RdfTermKind.Iri && IsAbsolute(graph) && ManagementVocabulary.LocalName(graph) is null)
                {
                    explicitGraphs.Add(graph);
                }
                else
                {
                    Stated(node, ManagementVocabulary.Graph, "varve:graph is an absolute IRI or varve:defaultGraph");
                    ok = false;
                }
            }

            foreach (RdfTerm prefix in prefixes)
            {
                if (prefix.Kind != RdfTermKind.Literal || prefix.Lexical.IsEmpty)
                {
                    Stated(node, ManagementVocabulary.GraphPrefix, "varve:graphPrefix is a non-empty literal");
                    ok = false;
                    continue;
                }

                prefixValues.Add(Encoding.UTF8.GetString(prefix.Lexical));
            }

            foreach (RdfTerm template in templates)
            {
                if (Template(template) is { } read)
                {
                    templateValues.Add(read);
                }
                else
                {
                    ok = false;
                }
            }

            return ok
                ? new GraphSet { DefaultGraph = includesDefault, Graphs = explicitGraphs, Prefixes = prefixValues, Templates = templateValues }
                : null;
        }

        private GraphTemplate? Template(RdfTerm node)
        {
            if (!Has(node, ManagementVocabulary.Type, ManagementVocabulary.GraphTemplate))
            {
                Stated(node, ManagementVocabulary.GraphTemplateProperty, "varve:graphTemplate names a varve:GraphTemplate declared in the same graph");
                return null;
            }

            string? template = Literal(node, ManagementVocabulary.Template, required: true);
            TemplateMatch match = One(node, ManagementVocabulary.Match) switch
            {
                { } value when value.Equals(ManagementVocabulary.Exact) => TemplateMatch.Exact,
                { } value when value.Equals(ManagementVocabulary.Prefix) => TemplateMatch.Prefix,
                _ => Invalid(TemplateMatch.Exact, node, ManagementVocabulary.Match, "varve:match is varve:Exact or varve:Prefix"),
            };

            if (template is null)
            {
                return null;
            }

            if (GraphTemplates.Check(template) is { } problem)
            {
                Residual(node, ManagementVocabulary.Template, problem);
                return null;
            }

            return new GraphTemplate(template, match);
        }

        private IEnumerable<RdfTerm> Typed(RdfTerm type)
        {
            foreach ((RdfTerm node, List<(RdfTerm Predicate, RdfTerm Object)> pairs) in _nodes)
            {
                foreach ((RdfTerm predicate, RdfTerm value) in pairs)
                {
                    if (predicate.Equals(ManagementVocabulary.Type) && value.Equals(type))
                    {
                        yield return node;
                        break;
                    }
                }
            }
        }

        private List<RdfTerm> Values(RdfTerm node, RdfTerm predicate)
        {
            List<RdfTerm> values = [];

            if (_nodes.TryGetValue(node, out List<(RdfTerm Predicate, RdfTerm Object)>? pairs))
            {
                foreach ((RdfTerm p, RdfTerm o) in pairs)
                {
                    if (p.Equals(predicate))
                    {
                        values.Add(o);
                    }
                }
            }

            return values;
        }

        private bool Has(RdfTerm node, RdfTerm predicate, RdfTerm value) => Values(node, predicate).Exists(v => v.Equals(value));

        private RdfTerm? One(RdfTerm node, RdfTerm predicate)
        {
            List<RdfTerm> values = Values(node, predicate);

            if (values.Count > 1)
            {
                Stated(node, predicate, "this property is stated at most once; here it is stated " + values.Count + " times");
            }

            return values.Count == 0 ? null : values[0];
        }

        private string? Literal(RdfTerm node, RdfTerm predicate, bool required)
        {
            RdfTerm? value = One(node, predicate);

            if (value is null)
            {
                if (required)
                {
                    Stated(node, predicate, "this property is required");
                }

                return null;
            }

            if (value.Kind != RdfTermKind.Literal || !value.Language.IsEmpty || (value.Datatype is { } datatype && !datatype.Lexical.SequenceEqual("http://www.w3.org/2001/XMLSchema#string"u8)))
            {
                Stated(node, predicate, "this property's value is a plain string literal");
                return null;
            }

            return Encoding.UTF8.GetString(value.Lexical);
        }

        private bool? Boolean(RdfTerm node, RdfTerm predicate)
        {
            RdfTerm? value = One(node, predicate);

            if (value is null)
            {
                return null;
            }

            if (value.Kind == RdfTermKind.Literal && value.DatatypeIri.SequenceEqual(Encoding.UTF8.GetBytes(ManagementVocabulary.XsdBoolean)))
            {
                if (value.Lexical.SequenceEqual("true"u8) || value.Lexical.SequenceEqual("1"u8))
                {
                    return true;
                }

                if (value.Lexical.SequenceEqual("false"u8) || value.Lexical.SequenceEqual("0"u8))
                {
                    return false;
                }
            }

            Stated(node, predicate, "this property's value is an xsd:boolean");
            return null;
        }

        private T Invalid<T>(T fallback, RdfTerm node, RdfTerm predicate, string message)
        {
            Stated(node, predicate, message);
            return fallback;
        }

        /// <summary>A finding only the code can see (ADR 0175 point 3): reported here.</summary>
        private void Residual(RdfTerm? focus, RdfTerm? path, string message) => _findings.Add(new DeclarationFinding(_graph, focus, path, message));

        /// <summary>
        /// A rule the management shapes state: the shapes report it, from the
        /// same graph, and the reader only stops reading what it is wrong
        /// about (ADR 0175).
        /// </summary>
        private void Stated(RdfTerm? focus, RdfTerm? path, string message) => StatedByShapes.Add(new DeclarationFinding(_graph, focus, path, message));

        /// <summary>What the reader saw that the shapes state: not reported, since the shapes' results are.</summary>
        internal List<DeclarationFinding> StatedByShapes { get; } = [];

        private static bool IsAbsolute(RdfTerm iri) =>
            iri.Kind == RdfTermKind.Iri && IriRef.IsAbsolute(iri.Lexical);
    }
}
