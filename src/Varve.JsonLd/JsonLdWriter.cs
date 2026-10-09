// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.JsonLd.Json;
using Varve.JsonLd.Model;
using Varve.JsonLd.Processing;
using Varve.Rdf;
using Varve.Xsd;

namespace Varve.JsonLd;

/// <summary>
/// Writes an RDF dataset as expanded JSON-LD (JSON-LD 1.1 API §8.1, Serialize
/// RDF as JSON-LD; the <c>fromRdf</c> of the specification's test suite). The
/// whole dataset must be seen before a list can be detected or a subject
/// grouped, so quads are buffered as indices into one term arena and the
/// document is written on <see cref="Flush"/> or <see cref="Dispose"/>
/// (ADR 0112, <c>FromRdfBuffersTheDataset</c>).
/// </summary>
[DesignDecision(typeof(JsonLdOverUtf8Json.FromRdfBuffersTheDataset), Scope = ExceptionScope.HotPath)]
public sealed class JsonLdWriter : IDisposable
{
    private static ReadOnlySpan<byte> RdfType => "http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8;
    private static ReadOnlySpan<byte> RdfFirst => "http://www.w3.org/1999/02/22-rdf-syntax-ns#first"u8;
    private static ReadOnlySpan<byte> RdfRest => "http://www.w3.org/1999/02/22-rdf-syntax-ns#rest"u8;
    private static ReadOnlySpan<byte> RdfNil => "http://www.w3.org/1999/02/22-rdf-syntax-ns#nil"u8;
    private static ReadOnlySpan<byte> RdfList => "http://www.w3.org/1999/02/22-rdf-syntax-ns#List"u8;
    private static ReadOnlySpan<byte> RdfJson => "http://www.w3.org/1999/02/22-rdf-syntax-ns#JSON"u8;
    private static ReadOnlySpan<byte> RdfValue => "http://www.w3.org/1999/02/22-rdf-syntax-ns#value"u8;
    private static ReadOnlySpan<byte> RdfLanguage => "http://www.w3.org/1999/02/22-rdf-syntax-ns#language"u8;
    private static ReadOnlySpan<byte> RdfDirectionIri => "http://www.w3.org/1999/02/22-rdf-syntax-ns#direction"u8;
    private static ReadOnlySpan<byte> XsdStringIri => "http://www.w3.org/2001/XMLSchema#string"u8;
    private static ReadOnlySpan<byte> XsdBooleanIri => "http://www.w3.org/2001/XMLSchema#boolean"u8;
    private static ReadOnlySpan<byte> XsdIntegerIri => "http://www.w3.org/2001/XMLSchema#integer"u8;
    private static ReadOnlySpan<byte> XsdDoubleIri => "http://www.w3.org/2001/XMLSchema#double"u8;
    private static ReadOnlySpan<byte> I18n => "https://www.w3.org/ns/i18n#"u8;

    private const byte KindIri = 0;
    private const byte KindBlank = 1;
    private const byte KindLiteral = 2;

    private readonly IBufferWriter<byte> _output;
    private readonly JsonLdWriteOptions _options;
    private readonly NameTable _names = new();
    private readonly JsonTree _tree;
    private readonly NameTable _terms = new();
    private readonly SubjectComparer _comparer;

    // Terms, by id from _terms (the keyword ids at the front are unused).
    private byte[] _kind = ArrayPool<byte>.Shared.Rent(256);
    private int[] _lexicalStart = ArrayPool<int>.Shared.Rent(256);
    private int[] _lexicalLength = ArrayPool<int>.Shared.Rent(256);
    private int[] _datatypeStart = ArrayPool<int>.Shared.Rent(256);
    private int[] _datatypeLength = ArrayPool<int>.Shared.Rent(256);
    private int[] _languageStart = ArrayPool<int>.Shared.Rent(256);
    private int[] _languageLength = ArrayPool<int>.Shared.Rent(256);
    private byte[] _direction = ArrayPool<byte>.Shared.Rent(256);
    private byte[] _key = ArrayPool<byte>.Shared.Rent(1024);

    // Quads, as term ids; -1 for the default graph.
    private int[] _graph = ArrayPool<int>.Shared.Rent(256);
    private int[] _subject = ArrayPool<int>.Shared.Rent(256);
    private int[] _predicate = ArrayPool<int>.Shared.Rent(256);
    private int[] _object = ArrayPool<int>.Shared.Rent(256);
    private int _quadCount;

    private bool _written;
    private bool _disposed;

    /// <summary>Creates a writer over <paramref name="output"/>; the document is written when the writer is flushed or disposed.</summary>
    public JsonLdWriter(IBufferWriter<byte> output, in JsonLdWriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
        _options = options;
        _tree = new JsonTree(_names);
        _comparer = new SubjectComparer(this);
    }

    /// <summary>Buffers one quad.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public void Write(in QuadView quad)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_written)
        {
            throw new InvalidOperationException("The document has been written; a JsonLdWriter writes one document.");
        }

        if (_quadCount == _graph.Length)
        {
            Grow(ref _graph);
            Grow(ref _subject);
            Grow(ref _predicate);
            Grow(ref _object);
        }

        _graph[_quadCount] = quad.HasGraph ? Intern(quad.Graph) : -1;
        _subject[_quadCount] = Intern(quad.Subject);
        _predicate[_quadCount] = Intern(quad.Predicate);
        _object[_quadCount] = Intern(quad.Object);
        _quadCount++;
    }

    /// <summary>Writes the document, once.</summary>
    public void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_written)
        {
            return;
        }

        _written = true;
        int root;

        try
        {
            root = Serialize();
        }
        catch (JsonLdException exception)
        {
            // The one error the algorithm can meet: an rdf:JSON literal whose
            // lexical form is not JSON. The writer refuses by name, as every
            // Varve writer does.
            throw new InvalidOperationException(exception.ToError().ToString(), exception);
        }

        JsonTreeWriter.Write(_output, _tree, root, _options.Indent);
    }

    /// <summary>Writes the document if it has not been, and returns the buffers.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            Flush();
        }
        finally
        {
            _disposed = true;
            ArrayPool<byte>.Shared.Return(_kind);
            ArrayPool<int>.Shared.Return(_lexicalStart);
            ArrayPool<int>.Shared.Return(_lexicalLength);
            ArrayPool<int>.Shared.Return(_datatypeStart);
            ArrayPool<int>.Shared.Return(_datatypeLength);
            ArrayPool<int>.Shared.Return(_languageStart);
            ArrayPool<int>.Shared.Return(_languageLength);
            ArrayPool<byte>.Shared.Return(_direction);
            ArrayPool<byte>.Shared.Return(_key);
            ArrayPool<int>.Shared.Return(_graph);
            ArrayPool<int>.Shared.Return(_subject);
            ArrayPool<int>.Shared.Return(_predicate);
            ArrayPool<int>.Shared.Return(_object);
            _tree.Reset();
        }
    }

    // ------------------------------------------------------------- terms

    /// <summary>Interns a term by its bytes: kind, lexical, datatype, language and direction, separated by bytes no term holds.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private int Intern(in RdfTermView view)
    {
        if (view.Kind == RdfTermKind.TripleTerm)
        {
            throw new InvalidOperationException("JSON-LD has no triple terms; the quad cannot be written.");
        }

        ReadOnlySpan<byte> lexical = view.Lexical;
        ReadOnlySpan<byte> datatype = view.Kind == RdfTermKind.Literal && view.HasDatatype ? view.Datatype : default;
        ReadOnlySpan<byte> language = view.Kind == RdfTermKind.Literal && view.HasLanguage ? view.Language : default;
        int length = 1 + lexical.Length + 1 + datatype.Length + 1 + language.Length + 1;

        if (_key.Length < length)
        {
            ArrayPool<byte>.Shared.Return(_key);
            _key = ArrayPool<byte>.Shared.Rent(length);
        }

        Span<byte> key = _key.AsSpan(0, length);
        int at = 0;
        key[at++] = view.Kind == RdfTermKind.Iri ? KindIri : view.Kind == RdfTermKind.BlankNode ? KindBlank : KindLiteral;
        lexical.CopyTo(key[at..]);
        at += lexical.Length;
        key[at++] = 0;
        datatype.CopyTo(key[at..]);
        at += datatype.Length;
        key[at++] = 0;
        language.CopyTo(key[at..]);
        at += language.Length;
        key[at++] = (byte)view.Direction;

        int before = _terms.Count;
        int id = _terms.Intern(key);

        if (_terms.Count == before)
        {
            return id;
        }

        if (id >= _kind.Length)
        {
            Grow(ref _kind);
            Grow(ref _lexicalStart);
            Grow(ref _lexicalLength);
            Grow(ref _datatypeStart);
            Grow(ref _datatypeLength);
            Grow(ref _languageStart);
            Grow(ref _languageLength);
            Grow(ref _direction);
        }

        _kind[id] = key[0];
        TextRange lexicalRange = _tree.AddText(lexical);
        _lexicalStart[id] = lexicalRange.Start;
        _lexicalLength[id] = lexicalRange.Length;
        TextRange datatypeRange = datatype.IsEmpty ? TextRange.None : _tree.AddText(datatype);
        _datatypeStart[id] = datatypeRange.Start;
        _datatypeLength[id] = datatypeRange.Length;
        TextRange languageRange = language.IsEmpty ? TextRange.None : _tree.AddText(language);
        _languageStart[id] = languageRange.Start;
        _languageLength[id] = languageRange.Length;
        _direction[id] = (byte)view.Direction;
        return id;
    }

    private TextRange Lexical(int term) => new(_lexicalStart[term], _lexicalLength[term]);

    /// <summary>A node's @id: the IRI, or the blank node label behind <c>_:</c>.</summary>
    private int IdNode(int term) => IsBlank(term) ? _tree.AddString(_tree.AddText("_:"u8, _tree.Bytes(Lexical(term)))) : _tree.AddString(Lexical(term));

    private TextRange Datatype(int term) => new(_datatypeStart[term], _datatypeLength[term]);

    private TextRange Language(int term) => new(_languageStart[term], _languageLength[term]);

    private bool IsNode(int term) => _kind[term] != KindLiteral;

    private bool IsBlank(int term) => _kind[term] == KindBlank;

    private bool PredicateIs(int term, ReadOnlySpan<byte> iri) => _kind[term] == KindIri && _tree.Bytes(Lexical(term)).SequenceEqual(iri);

    /// <summary>The id of the IRI term, or -1 when no quad holds it.</summary>
    private int FindIri(ReadOnlySpan<byte> iri)
    {
        int length = iri.Length + 4;

        if (_key.Length < length)
        {
            ArrayPool<byte>.Shared.Return(_key);
            _key = ArrayPool<byte>.Shared.Rent(length);
        }

        Span<byte> key = _key.AsSpan(0, length);
        key[0] = KindIri;
        iri.CopyTo(key[1..]);
        key[iri.Length + 1] = 0;
        key[iri.Length + 2] = 0;
        key[iri.Length + 3] = 0;
        return _terms.Find(key);
    }

    // --------------------------------------------------------- serialise

    /// <summary>One graph's node map: a node per subject term, and the usages lists detect lists from.</summary>
    private sealed class Graph
    {
        public int Name;
        public int[] NodeOf;

        /// <summary>Per term, the order the node was first a subject in this graph (or named a graph), -1 when it never was.</summary>
        public int[] Order;
        public int Created;

        /// <summary>The usages of this graph's rdf:nil node, where list detection starts (§8.1 step 4.3).</summary>
        public int NilUsages = -1;
        public bool[] Compound;
        public int[] CompoundValue;
        public int[] CompoundLanguage;
        public int[] CompoundDirection;

        public Graph(int name, int terms)
        {
            Name = name;
            NodeOf = ArrayPool<int>.Shared.Rent(terms);
            NodeOf.AsSpan(0, terms).Fill(-1);
            Order = ArrayPool<int>.Shared.Rent(terms);
            Order.AsSpan(0, terms).Fill(-1);
            Compound = ArrayPool<bool>.Shared.Rent(terms);
            Compound.AsSpan(0, terms).Clear();
            CompoundValue = ArrayPool<int>.Shared.Rent(terms);
            CompoundValue.AsSpan(0, terms).Fill(-1);
            CompoundLanguage = ArrayPool<int>.Shared.Rent(terms);
            CompoundLanguage.AsSpan(0, terms).Fill(-1);
            CompoundDirection = ArrayPool<int>.Shared.Rent(terms);
            CompoundDirection.AsSpan(0, terms).Fill(-1);
        }

        public void Return()
        {
            ArrayPool<int>.Shared.Return(NodeOf);
            ArrayPool<int>.Shared.Return(Order);
            ArrayPool<bool>.Shared.Return(Compound);
            ArrayPool<int>.Shared.Return(CompoundValue);
            ArrayPool<int>.Shared.Return(CompoundLanguage);
            ArrayPool<int>.Shared.Return(CompoundDirection);
        }
    }

    // Usages: the node (as its term, in its graph), the property and the
    // value object that reference a node. rdf:nil's are a linked list per
    // graph; a blank node's is the one in "referenced once" (§8.1 step
    // 3.3.9–3.3.10), which is global to the dataset: a list node shared by
    // two graphs is not a list (fromRdf-0020).
    private int[] _usageNode = ArrayPool<int>.Shared.Rent(256);
    private int[] _usageGraph = ArrayPool<int>.Shared.Rent(256);
    private int[] _usageProperty = ArrayPool<int>.Shared.Rent(256);
    private int[] _usageValue = ArrayPool<int>.Shared.Rent(256);
    private int[] _usageNext = ArrayPool<int>.Shared.Rent(256);
    private int _usageCount;

    /// <summary>Per blank node term: -1 never referenced, -2 referenced more than once, else its one usage.</summary>
    private int[] _referencedOnce = ArrayPool<int>.Shared.Rent(256);
    private List<Graph> _graphs = [];

    [DesignDecision(typeof(JsonLdOverUtf8Json.FromRdfBuffersTheDataset), Scope = ExceptionScope.HotPath)]
    private int Serialize()
    {
        int terms = _terms.Count;
        List<Graph> graphs = [];
        _graphs = graphs;
        Graph defaultGraph = new(-1, terms);
        graphs.Add(defaultGraph);

        if (_referencedOnce.Length < terms)
        {
            ArrayPool<int>.Shared.Return(_referencedOnce);
            _referencedOnce = ArrayPool<int>.Shared.Rent(terms);
        }

        _referencedOnce.AsSpan(0, terms).Fill(-1);
        int nilTerm = FindIri(RdfNil);

        // 1.–3. the node maps, one per graph, quads grouped by graph by a
        // sort that keeps their arrival order within a graph and orders the
        // graphs by the first quad each was the graph of: the key is that
        // quad's index and the quad's own, so that the document is a
        // function of the quads in the order they came (Array.Sort alone is
        // not stable), and a reader of the document meets the graphs in the
        // same order.
        int[] order = ArrayPool<int>.Shared.Rent(Math.Max(_quadCount, 1));
        long[] keys = ArrayPool<long>.Shared.Rent(Math.Max(_quadCount, 1));
        int[] firstAsGraph = ArrayPool<int>.Shared.Rent(terms);
        firstAsGraph.AsSpan(0, terms).Fill(-1);

        for (int i = 0; i < _quadCount; i++)
        {
            if (_graph[i] >= 0 && firstAsGraph[_graph[i]] < 0)
            {
                firstAsGraph[_graph[i]] = i;
            }
        }

        for (int i = 0; i < _quadCount; i++)
        {
            order[i] = i;
            keys[i] = ((long)(_graph[i] < 0 ? 0 : firstAsGraph[_graph[i]] + 1) << 32) | (uint)i;
        }

        Array.Sort(keys, order, 0, _quadCount);
        ArrayPool<int>.Shared.Return(firstAsGraph);

        int at = 0;

        while (at < _quadCount)
        {
            int name = _graph[order[at]];
            int end = at;

            while (end < _quadCount && _graph[order[end]] == name)
            {
                end++;
            }

            Graph graph = name < 0 ? defaultGraph : new Graph(name, terms);

            if (name >= 0)
            {
                graphs.Add(graph);
                Node(defaultGraph, name);
                Subject(defaultGraph, name);
            }

            FillGraph(graph, name < 0 ? 0 : graphs.Count - 1, order, at, end, nilTerm);
            at = end;
        }

        ArrayPool<int>.Shared.Return(order);
        ArrayPool<long>.Shared.Return(keys);

        // 4. lists.
        foreach (Graph graph in graphs)
        {
            ConvertLists(graph);
        }

        // 5. and 6. the result: the default graph's subjects in order, each with its named graph.
        int result = _tree.AddArray();
        int[] subjects = Subjects(defaultGraph, terms, out int subjectCount);

        for (int i = 0; i < subjectCount; i++)
        {
            int subject = subjects[i];
            int node = defaultGraph.NodeOf[subject];

            foreach (Graph graph in graphs)
            {
                if (graph.Name != subject)
                {
                    continue;
                }

                int graphArray = _tree.AddArray();
                int[] inner = Subjects(graph, terms, out int innerCount);

                for (int j = 0; j < innerCount; j++)
                {
                    int innerNode = graph.NodeOf[inner[j]];

                    if (!_tree.HasOnly(innerNode, Keyword.Id))
                    {
                        _tree.SortMembers(innerNode);
                        _tree.Append(graphArray, innerNode);
                    }
                }

                ArrayPool<int>.Shared.Return(inner);
                _tree.AddMember(node, Keyword.Graph, graphArray);
            }

            if (!_tree.HasOnly(node, Keyword.Id))
            {
                // Members in a fixed order, so that the document is a function
                // of the dataset and not of the order its quads arrived in:
                // write, read, write gives the same bytes (json-ld.md §6).
                _tree.SortMembers(node);
                _tree.Append(result, node);
            }
        }

        ArrayPool<int>.Shared.Return(subjects);

        foreach (Graph graph in graphs)
        {
            graph.Return();
        }

        return result;
    }

    private void FillGraph(Graph graph, int graphIndex, int[] order, int from, int to, int nilTerm)
    {
        // The compound literals (rdfDirection compound-literal): blank
        // subjects of rdf:direction, read before the triples that reference them.
        if (_options.RdfDirection == RdfDirection.CompoundLiteral)
        {
            for (int i = from; i < to; i++)
            {
                int q = order[i];

                if (IsBlank(_subject[q]) && PredicateIs(_predicate[q], RdfDirectionIri) && !IsNode(_object[q]))
                {
                    graph.Compound[_subject[q]] = true;
                    graph.CompoundDirection[_subject[q]] = _object[q];
                }
            }

            for (int i = from; i < to; i++)
            {
                int q = order[i];

                if (!graph.Compound[_subject[q]] || IsNode(_object[q]))
                {
                    continue;
                }

                if (PredicateIs(_predicate[q], RdfValue))
                {
                    graph.CompoundValue[_subject[q]] = _object[q];
                }
                else if (PredicateIs(_predicate[q], RdfLanguage))
                {
                    graph.CompoundLanguage[_subject[q]] = _object[q];
                }
            }
        }

        for (int i = from; i < to; i++)
        {
            int q = order[i];
            int s = _subject[q], p = _predicate[q], o = _object[q];

            if (graph.Compound[s])
            {
                continue;
            }

            int node = Node(graph, s);
            Subject(graph, s);
            bool objectIsNode = IsNode(o) && !graph.Compound[o];

            if (objectIsNode)
            {
                Node(graph, o);
            }

            if (PredicateIs(p, RdfType) && objectIsNode && !_options.UseRdfType)
            {
                int types = _tree.Member(node, Keyword.Type);

                if (types < 0)
                {
                    types = _tree.AddArray();
                    _tree.AddMember(node, Keyword.Type, types);
                }

                bool present = false;

                int typeNode = IdNode(o);

                for (int type = _tree.First(types); type >= 0; type = _tree.Next(type))
                {
                    if (_tree.Bytes(type).SequenceEqual(_tree.Bytes(typeNode)))
                    {
                        present = true;
                        break;
                    }
                }

                if (!present)
                {
                    _tree.Append(types, typeNode);
                }

                continue;
            }

            int value = graph.Compound[o] ? CompoundValue(graph, o) : ToObject(o);
            int propertyName = _names.Intern(_tree.Bytes(Lexical(p)));
            int values = _tree.Member(node, propertyName);

            if (values < 0)
            {
                values = _tree.AddArray();
                _tree.AddMember(node, propertyName, values);
            }

            bool duplicate = false;

            for (int existing = _tree.First(values); existing >= 0; existing = _tree.Next(existing))
            {
                if (_tree.SameValue(existing, value))
                {
                    duplicate = true;
                    break;
                }
            }

            if (duplicate)
            {
                continue;
            }

            _tree.Append(values, value);

            if (o == nilTerm)
            {
                graph.NilUsages = AddUsage(s, graphIndex, p, value, graph.NilUsages);
            }
            else if (objectIsNode && IsBlank(o))
            {
                _referencedOnce[o] = _referencedOnce[o] == -1 ? AddUsage(s, graphIndex, p, value, -1) : -2;
            }
        }
    }

    /// <summary>Records the order a term was first a subject in a graph, which is how its blank nodes are ordered on output.</summary>
    private static void Subject(Graph graph, int term)
    {
        if (graph.Order[term] < 0)
        {
            graph.Order[term] = graph.Created++;
        }
    }

    /// <summary>The node object for a term in a graph, created as <c>{"@id": term}</c> on first sight.</summary>
    private int Node(Graph graph, int term)
    {
        int node = graph.NodeOf[term];

        if (node < 0)
        {
            node = _tree.AddObject();
            _tree.AddMember(node, Keyword.Id, IdNode(term));
            graph.NodeOf[term] = node;
        }

        return node;
    }

    /// <summary>Records a usage and returns its index; <paramref name="next"/> chains it.</summary>
    private int AddUsage(int nodeTerm, int graphIndex, int property, int value, int next)
    {
        if (_usageCount == _usageNode.Length)
        {
            Grow(ref _usageNode);
            Grow(ref _usageGraph);
            Grow(ref _usageProperty);
            Grow(ref _usageValue);
            Grow(ref _usageNext);
        }

        _usageNode[_usageCount] = nodeTerm;
        _usageGraph[_usageCount] = graphIndex;
        _usageProperty[_usageCount] = property;
        _usageValue[_usageCount] = value;
        _usageNext[_usageCount] = next;
        return _usageCount++;
    }

    /// <summary>§8.1 step 4: every well-formed <c>rdf:first</c>/<c>rdf:rest</c> chain ending in <c>rdf:nil</c> becomes a list object.</summary>
    private void ConvertLists(Graph graph)
    {
        if (graph.NilUsages < 0)
        {
            return;
        }

        int first = FindIri(RdfFirst);
        int rest = FindIri(RdfRest);
        int firstName = first < 0 ? -1 : _names.Intern(_tree.Bytes(Lexical(first)));
        int restName = rest < 0 ? -1 : _names.Intern(_tree.Bytes(Lexical(rest)));

        for (int usage = graph.NilUsages; usage >= 0; usage = _usageNext[usage])
        {
            int nodeTerm = _usageNode[usage];
            Graph nodeGraph = _graphs[_usageGraph[usage]];
            int property = _usageProperty[usage];
            int head = _usageValue[usage];
            int list = _tree.AddArray();
            int[] listNodes = ArrayPool<int>.Shared.Rent(16);
            int[] listGraphs = ArrayPool<int>.Shared.Rent(16);
            int listNodeCount = 0;

            while (property == rest && IsBlank(nodeTerm) && _referencedOnce[nodeTerm] >= 0 && IsListNode(nodeGraph.NodeOf[nodeTerm], firstName, restName))
            {
                int node = nodeGraph.NodeOf[nodeTerm];
                _tree.Append(list, _tree.First(_tree.Member(node, firstName)));

                if (listNodeCount == listNodes.Length)
                {
                    Grow(ref listNodes);
                    Grow(ref listGraphs);
                }

                listNodes[listNodeCount] = nodeTerm;
                listGraphs[listNodeCount] = _usageGraph[_referencedOnce[nodeTerm]];
                listNodeCount++;
                int nodeUsage = _referencedOnce[nodeTerm];
                nodeTerm = _usageNode[nodeUsage];
                nodeGraph = _graphs[_usageGraph[nodeUsage]];
                property = _usageProperty[nodeUsage];
                head = _usageValue[nodeUsage];

                if (!IsBlank(nodeTerm))
                {
                    break;
                }
            }

            // The list was appended tail first; reverse it into @list.
            int reversed = _tree.AddArray();
            ReverseInto(list, reversed);
            _tree.RemoveMember(head, Keyword.Id);
            _tree.SetMember(head, Keyword.List, reversed);

            for (int i = 0; i < listNodeCount; i++)
            {
                _graphs[listGraphs[i]].NodeOf[listNodes[i]] = -1;
            }

            ArrayPool<int>.Shared.Return(listNodes);
            ArrayPool<int>.Shared.Return(listGraphs);
        }
    }

    private void ReverseInto(int list, int reversed)
    {
        int count = _tree.Count(list);

        if (count == 0)
        {
            return;
        }

        int[] items = ArrayPool<int>.Shared.Rent(count);
        int i = 0;

        for (int item = _tree.First(list); item >= 0; item = _tree.Next(item))
        {
            items[i++] = item;
        }

        for (int j = count - 1; j >= 0; j--)
        {
            _tree.Append(reversed, items[j]);
        }

        ArrayPool<int>.Shared.Return(items);
    }

    /// <summary>A list node: @id, rdf:first and rdf:rest each with one value, and at most @type [rdf:List] besides.</summary>
    private bool IsListNode(int node, int firstName, int restName)
    {
        if (node < 0 || firstName < 0 || restName < 0)
        {
            return false;
        }

        int first = _tree.Member(node, firstName);
        int rest = _tree.Member(node, restName);

        if (!_tree.IsArray(first) || _tree.Count(first) != 1 || !_tree.IsArray(rest) || _tree.Count(rest) != 1)
        {
            return false;
        }

        int expected = 3;
        int types = _tree.Member(node, Keyword.Type);

        if (types >= 0)
        {
            if (_tree.Count(types) != 1 || !_tree.Bytes(_tree.First(types)).SequenceEqual(RdfList))
            {
                return false;
            }

            expected = 4;
        }

        return _tree.Count(node) == expected;
    }

    /// <summary>§8.2 RDF to object conversion.</summary>
    private int ToObject(int term)
    {
        if (IsNode(term))
        {
            int reference = _tree.AddObject();
            _tree.AddMember(reference, Keyword.Id, IdNode(term));
            return reference;
        }

        int result = _tree.AddObject();
        TextRange lexical = Lexical(term);
        TextRange datatype = Datatype(term);
        TextRange language = Language(term);
        TextDirection direction = (TextDirection)_direction[term];
        int converted = -1;
        TextRange type = TextRange.None;
        bool jsonType = false;
        TextRange outLanguage = TextRange.None;
        TextDirection outDirection = TextDirection.None;

        if (!datatype.IsNone && _tree.TextEquals(datatype, RdfJson))
        {
            converted = JsonTreeReader.Read(_tree.Bytes(lexical), _tree, JsonLdErrorCode.InvalidJsonLiteral);
            jsonType = true;
        }
        else if (_options.UseNativeTypes && !datatype.IsNone)
        {
            if (_tree.TextEquals(datatype, XsdStringIri))
            {
                converted = _tree.AddString(lexical);
            }
            else if (_tree.TextEquals(datatype, XsdBooleanIri))
            {
                // The lexical space is true, false, 1 and 0; anything else
                // stays a typed string (fromRdf-0027).
                if (XsdBoolean.TryParse(_tree.Bytes(lexical), out XsdBoolean boolean))
                {
                    converted = _tree.AddBoolean(boolean.Value);
                }
                else
                {
                    converted = _tree.AddString(lexical);
                    type = datatype;
                }
            }
            else if (_tree.TextEquals(datatype, XsdIntegerIri) && XsdInteger.TryParse(_tree.Bytes(lexical), out XsdInteger integer))
            {
                Span<byte> digits = _tree.ReserveText(32);
                integer.TryFormat(digits, out int written);
                converted = _tree.AddNumber(_tree.CommitText(written));
            }
            else if (_tree.TextEquals(datatype, XsdDoubleIri) && XsdDouble.TryParse(_tree.Bytes(lexical), out XsdDouble value) && !value.IsNaN && !double.IsInfinity(value.Value))
            {
                Span<byte> digits = _tree.ReserveText(48);
                int written = Jcs.FormatEcmaScript(value.Value, digits);
                converted = _tree.AddNumber(_tree.CommitText(written));
            }
            else
            {
                converted = _tree.AddString(lexical);
                type = datatype;
            }
        }
        else if (!language.IsNone)
        {
            converted = _tree.AddString(lexical);
            outLanguage = Lowercase(language);

            if (direction != TextDirection.None && _options.RdfDirection != RdfDirection.None)
            {
                outDirection = direction;
            }
        }
        else if (_options.RdfDirection == RdfDirection.I18nDatatype && !datatype.IsNone && _tree.Bytes(datatype).StartsWith(I18n))
        {
            converted = _tree.AddString(lexical);
            ReadOnlySpan<byte> fragment = _tree.Bytes(datatype)[I18n.Length..];
            int underscore = fragment.IndexOf((byte)'_');

            if (underscore >= 0)
            {
                if (underscore > 0)
                {
                    outLanguage = Lowercase(_tree.AddText(fragment[..underscore]));
                }

                ReadOnlySpan<byte> dir = fragment[(underscore + 1)..];
                outDirection = dir.SequenceEqual("ltr"u8) ? TextDirection.LeftToRight : dir.SequenceEqual("rtl"u8) ? TextDirection.RightToLeft : TextDirection.None;
            }
        }
        else
        {
            converted = _tree.AddString(lexical);

            if (!datatype.IsNone && !_tree.TextEquals(datatype, XsdStringIri))
            {
                type = datatype;
            }
        }

        _tree.AddMember(result, Keyword.Value, converted);

        if (jsonType)
        {
            _tree.AddMember(result, Keyword.Type, _tree.AddStringOfName(Keyword.Json));
        }
        else if (!type.IsNone)
        {
            _tree.AddMember(result, Keyword.Type, _tree.AddString(type));
        }
        else
        {
            if (!outLanguage.IsNone)
            {
                _tree.AddMember(result, Keyword.Language, _tree.AddString(outLanguage));
            }

            if (outDirection != TextDirection.None)
            {
                _tree.AddMember(result, Keyword.Direction, _tree.AddString(outDirection == TextDirection.LeftToRight ? "ltr"u8 : "rtl"u8));
            }
        }

        return result;
    }

    /// <summary>A compound literal's value object: rdf:value, rdf:language and rdf:direction.</summary>
    private int CompoundValue(Graph graph, int term)
    {
        int result = _tree.AddObject();
        int value = graph.CompoundValue[term];
        _tree.AddMember(result, Keyword.Value, _tree.AddString(value < 0 ? TextRange.None : Lexical(value)));

        if (graph.CompoundLanguage[term] >= 0)
        {
            _tree.AddMember(result, Keyword.Language, _tree.AddString(Lowercase(Lexical(graph.CompoundLanguage[term]))));
        }

        if (graph.CompoundDirection[term] >= 0)
        {
            _tree.AddMember(result, Keyword.Direction, _tree.AddString(Lexical(graph.CompoundDirection[term])));
        }

        return result;
    }

    [DesignDecision(typeof(JsonLdOverUtf8Json.LanguageTagsAreLowercased), Scope = ExceptionScope.HotPath)]
    private TextRange Lowercase(TextRange range)
    {
        ReadOnlySpan<byte> text = _tree.Bytes(range);
        Span<byte> destination = _tree.ReserveText(text.Length);
        text = _tree.Bytes(range);

        for (int i = 0; i < text.Length; i++)
        {
            byte b = text[i];
            destination[i] = b >= (byte)'A' && b <= (byte)'Z' ? (byte)(b | 0x20) : b;
        }

        return _tree.CommitText(text.Length);
    }

    /// <summary>
    /// The graph's subjects that still have a node, IRIs first in
    /// lexicographical order and blank nodes after them in the order they
    /// were first a subject in the graph's quads; returned in a rented
    /// array. Blank node labels are not ordered by their text because a
    /// reader renumbers them, and not by first mention because a reader of
    /// this document mentions them in its order: the order of first use as a
    /// subject is the one a reader reproduces, which is what makes write,
    /// read, write give the same bytes up to the labels.
    /// </summary>
    private int[] Subjects(Graph graph, int terms, out int count)
    {
        int[] subjects = ArrayPool<int>.Shared.Rent(Math.Max(terms, 1));
        count = 0;

        for (int term = Keyword.Count; term < terms; term++)
        {
            if (graph.NodeOf[term] >= 0)
            {
                subjects[count++] = term;
            }
        }

        _comparer.Graph = graph;
        Array.Sort(subjects, 0, count, _comparer);
        return subjects;
    }

    private sealed class SubjectComparer(JsonLdWriter writer) : IComparer<int>
    {
        public Graph? Graph;

        public int Compare(int x, int y)
        {
            bool xBlank = writer.IsBlank(x);
            bool yBlank = writer.IsBlank(y);

            if (xBlank != yBlank)
            {
                return xBlank ? 1 : -1;
            }

            if (xBlank)
            {
                return Graph!.Order[x].CompareTo(Graph.Order[y]);
            }

            return JsonTree.CompareUtf16(writer._tree.Bytes(writer.Lexical(x)), writer._tree.Bytes(writer.Lexical(y)));
        }
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private static void Grow<T>(ref T[] array)
    {
        T[] bigger = ArrayPool<T>.Shared.Rent(array.Length * 2);
        array.AsSpan().CopyTo(bigger);
        ArrayPool<T>.Shared.Return(array);
        array = bigger;
    }
}
