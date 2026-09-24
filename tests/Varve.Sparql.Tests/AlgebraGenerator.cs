// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Varve.Rdf;
using Varve.Sparql.Algebra;

namespace Varve.Sparql.Tests;

/// <summary>
/// Random algebra trees inside the parser's image (<c>docs/spec/sparql-algebra.md</c>
/// §6): every node type, every operator, every 1.2 construct, under the
/// constraints the parser enforces — fresh variables per Extend and AS, group
/// keys as the only bare variables at a grouped level, unique blank node
/// labels, projections that are never empty, no empty BGP as a Join operand.
/// </summary>
/// <remarks>
/// Seeded, so that a failing seed reproduces; the depth budget grows with the
/// seed so that CsCheck's shrinking toward small seeds shrinks the trees.
/// </remarks>
internal sealed class AlgebraGenerator
{
    private static readonly RdfTerm XsdInteger = RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#integer"u8);
    private static readonly RdfTerm XsdDecimal = RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#decimal"u8);
    private static readonly RdfTerm XsdDouble = RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#double"u8);
    private static readonly RdfTerm XsdBoolean = RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#boolean"u8);
    private static readonly RdfTerm XsdDate = RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#date"u8);

    private static readonly string[] Pool = ["s", "p", "o", "x", "y", "g"];

    private readonly Random _random;
    private readonly int _budget;
    private int _fresh;
    private int _blank;

    internal AlgebraGenerator(int seed)
    {
        _random = new Random(seed);
        _budget = 1 + (Math.Abs(seed) % 4);
    }

    // --- entry points ------------------------------------------------------

    internal Query Query()
    {
        Prologue prologue = Prologue();
        DatasetSpec? dataset = Chance(4) ? Dataset() : null;

        switch (_random.Next(4))
        {
            case 0:
                return new SelectQuery(prologue, dataset, Level(_budget, projecting: true));
            case 1:
                return new ConstructQuery(prologue, dataset, Triples(0, 3, new TripleMode(true, true, false)), Level(_budget, projecting: false));
            case 2:
                return new AskQuery(prologue, dataset, Level(_budget, projecting: false));
            default:
                List<PatternTerm> resources = [];

                for (int i = _random.Next(1, 3); i > 0; i--)
                {
                    resources.Add(Chance(2) ? new VariablePattern(PoolVariable()) : new TermPattern(Iri()));
                }

                return new DescribeQuery(prologue, dataset, AlgebraList.From(resources), Level(_budget, projecting: false));
        }
    }

    internal Update Update()
    {
        List<UpdateOperation> operations = [];

        for (int i = _random.Next(0, 4); i > 0; i--)
        {
            operations.Add(Operation());
        }

        return new Update(Prologue(), AlgebraList.From(operations));
    }

    // --- prologue ----------------------------------------------------------

    private Prologue Prologue()
    {
        List<PrefixDeclaration> prefixes = [];

        if (Chance(2))
        {
            prefixes.Add(new PrefixDeclaration("ex", RdfTerm.Iri("http://example.org/"u8)));
        }

        if (Chance(3))
        {
            prefixes.Add(new PrefixDeclaration("", RdfTerm.Iri("http://example.org/default#"u8)));
        }

        RdfTerm? baseIri = Chance(4) ? RdfTerm.Iri("http://example.org/base/"u8) : null;
        SparqlVersion? version = Chance(3) ? SparqlVersion.Sparql12 : null;
        return new Prologue(baseIri, AlgebraList.From(prefixes), version);
    }

    // --- levels ------------------------------------------------------------

    /// <summary>A query level: the pattern, then Group, HAVING, VALUES, Extends, OrderBy, Project, Distinct, Slice, in the parser's nesting.</summary>
    private QueryPattern Level(int depth, bool projecting)
    {
        QueryPattern where = Pattern(depth);
        bool grouped = Chance(3);
        List<Variable> keyVariables = [];
        QueryPattern pattern = where;

        if (grouped)
        {
            List<GroupKey> keys = [];
            List<Variable> inScope = Scope(where);

            for (int i = _random.Next(0, 3); i > 0; i--)
            {
                switch (_random.Next(3))
                {
                    case 0 when inScope.Count > 0:
                        Variable plain = inScope[_random.Next(inScope.Count)];

                        if (!keyVariables.Contains(plain))
                        {
                            keys.Add(new GroupKey(new VariableExpression(plain), plain));
                            keyVariables.Add(plain);
                        }

                        break;
                    case 1:
                        Variable named = Fresh();
                        keys.Add(new GroupKey(Expression(2, null, aggregates: false), named));
                        keyVariables.Add(named);
                        break;
                    default:
                        keys.Add(new GroupKey(Expression(2, null, aggregates: false), null));
                        break;
                }
            }

            pattern = new Group(pattern, AlgebraList.From(keys));

            for (int i = _random.Next(0, 2); i > 0; i--)
            {
                pattern = new Filter(Expression(2, keyVariables, aggregates: true), pattern);
            }

            // Implicit grouping exists only because of an aggregate somewhere
            // above the Group; a form that projects gets it in a SELECT
            // expression below, any other form gets it in HAVING here.
            if (keyVariables.Count == 0 && !projecting)
            {
                pattern = new Filter(new BinaryExpression(BinaryOperator.Greater, Aggregate(keyVariables), Constant()), pattern);
            }
        }

        if (Chance(5))
        {
            pattern = pattern is Bgp { Triples.IsEmpty: true } ? Values() : new Join(pattern, Values());
        }

        List<Variable> projected = [];

        if (projecting)
        {
            List<Variable> candidates = grouped ? keyVariables : Scope(pattern);

            foreach (Variable candidate in candidates)
            {
                if (Chance(2))
                {
                    projected.Add(candidate);
                }
            }

            if (!grouped && Chance(4))
            {
                Variable unbound = PoolVariable();

                if (!projected.Contains(unbound))
                {
                    projected.Add(unbound);
                }
            }
        }

        int extends = projecting ? _random.Next(0, 3) : 0;
        bool needsAggregate = grouped && keyVariables.Count == 0 && projecting;

        for (int i = 0; i < extends || (needsAggregate && i == 0); i++)
        {
            Variable variable = Fresh();
            Expression expression = needsAggregate && i == 0
                ? Aggregate(keyVariables)
                : Expression(2, grouped ? keyVariables : null, aggregates: grouped);
            pattern = new Extend(pattern, variable, expression);
            projected.Add(variable);
        }

        if (projecting && projected.Count == 0)
        {
            Variable variable = Fresh();
            pattern = new Extend(pattern, variable, Expression(1, grouped ? keyVariables : null, aggregates: grouped));
            projected.Add(variable);
        }

        if (Chance(3))
        {
            List<OrderCondition> conditions = [];

            for (int i = _random.Next(1, 3); i > 0; i--)
            {
                conditions.Add(new OrderCondition(Expression(2, grouped ? keyVariables : null, aggregates: grouped), Chance(2)));
            }

            pattern = new OrderBy(pattern, AlgebraList.From(conditions));
        }

        if (projecting)
        {
            pattern = new Project(pattern, AlgebraList.From(projected));

            if (Chance(4))
            {
                pattern = Chance(2) ? new Distinct(pattern) : new Reduced(pattern);
            }
        }

        if (Chance(4))
        {
            pattern = Chance(2) ? new Slice(pattern, _random.Next(0, 10), null) : new Slice(pattern, _random.Next(0, 10), _random.Next(0, 100));
        }

        return pattern;
    }

    // --- patterns ----------------------------------------------------------

    private QueryPattern Pattern(int depth)
    {
        if (depth <= 0)
        {
            return Chance(4) ? new Bgp(default) : Leaf();
        }

        switch (_random.Next(12))
        {
            case 0:
                return new Join(NonEmpty(depth - 1), NonEmpty(depth - 1));
            case 1:
                return new LeftJoin(Pattern(depth - 1), Pattern(depth - 1), Chance(2) ? Expression(2, null, aggregates: false) : null);
            case 2:
                return new Filter(Expression(2, null, aggregates: false), Pattern(depth - 1));
            case 3:
                return new Union(Pattern(depth - 1), Pattern(depth - 1));
            case 4:
                return new Graph(Chance(2) ? new VariablePattern(PoolVariable()) : new TermPattern(Iri()), Pattern(depth - 1));
            case 5:
                return new Extend(Pattern(depth - 1), Fresh(), Expression(2, null, aggregates: false));
            case 6:
                return new Minus(Pattern(depth - 1), Pattern(depth - 1));
            case 7:
                return new Service(Chance(2) ? new VariablePattern(PoolVariable()) : new TermPattern(Iri()), Pattern(depth - 1), Chance(2));
            case 8:
                return Level(depth - 1, projecting: true);
            default:
                return Leaf();
        }
    }

    private QueryPattern NonEmpty(int depth)
    {
        QueryPattern pattern = Pattern(depth);
        return pattern is Bgp { Triples.IsEmpty: true } ? Leaf() : pattern;
    }

    private QueryPattern Leaf()
    {
        switch (_random.Next(5))
        {
            case 0:
                return new PathPattern(Node(new TripleMode(true, true, true)), CompoundPath(), Node(new TripleMode(true, true, true)));
            case 1:
                return Values();
            default:
                return new Bgp(Triples(1, 3, new TripleMode(true, true, true)));
        }
    }

    private Values Values()
    {
        List<Variable> variables = [];

        for (int i = _random.Next(0, 3); i > 0; i--)
        {
            Variable variable = PoolVariable();

            if (!variables.Contains(variable))
            {
                variables.Add(variable);
            }
        }

        List<AlgebraList<RdfTerm?>> rows = [];

        for (int i = _random.Next(0, 3); i > 0; i--)
        {
            List<RdfTerm?> row = [];

            foreach (Variable _ in variables)
            {
                row.Add(Chance(4) ? null : Chance(6) ? DataTripleTerm() : Term());
            }

            rows.Add(AlgebraList.From(row));
        }

        return new Values(AlgebraList.From(variables), AlgebraList.From(rows));
    }

    /// <summary>What a triple position may hold, by where the triple is.</summary>
    private readonly record struct TripleMode(bool Variables, bool BlankNodes, bool TripleTerms);

    private AlgebraList<TriplePattern> Triples(int min, int max, TripleMode mode)
    {
        List<TriplePattern> triples = [];

        for (int i = _random.Next(min, max + 1); i > 0; i--)
        {
            triples.Add(new TriplePattern(Node(mode), Predicate(mode), Node(mode)));
        }

        return AlgebraList.From(triples);
    }

    private PatternTerm Predicate(TripleMode mode) =>
        mode.Variables && Chance(3) ? new VariablePattern(PoolVariable()) : new TermPattern(Iri());

    private PatternTerm Node(TripleMode mode)
    {
        switch (_random.Next(8))
        {
            case 0 when mode.Variables:
            case 1 when mode.Variables:
            case 2 when mode.Variables:
                return new VariablePattern(PoolVariable());
            case 3 when mode.BlankNodes:
                return new BlankNodePattern("b" + (_blank++).ToString(CultureInfo.InvariantCulture));
            case 4 when mode.TripleTerms:
                return TripleTerm(mode, 1);
            case 5:
                return new TermPattern(Literal());
            default:
                return new TermPattern(Iri());
        }
    }

    private PatternTerm TripleTerm(TripleMode mode, int depth)
    {
        TripleMode inner = mode with { TripleTerms = depth > 0 };
        PatternTerm subject = Node(inner);
        PatternTerm predicate = Predicate(mode);
        PatternTerm @object = Node(inner);

        if (subject is TermPattern s && predicate is TermPattern p && @object is TermPattern o)
        {
            return new TermPattern(RdfTerm.TripleTerm(s.Term, p.Term, o.Term));
        }

        return new TripleTermPattern(subject, predicate, @object);
    }

    private RdfTerm DataTripleTerm() =>
        RdfTerm.TripleTerm(Iri(), Iri(), Chance(4) ? DataTripleTerm() : Term());

    // --- paths -------------------------------------------------------------

    /// <summary>A path the parser keeps as a path: not a bare predicate, nor the inverse of one, which become triples (§4.4).</summary>
    private PropertyPath CompoundPath()
    {
        while (true)
        {
            PropertyPath path = Path(2);

            if (path is not PredicatePath && path is not InversePath { Inner: PredicatePath })
            {
                return path;
            }
        }
    }

    private PropertyPath Path(int depth)
    {
        if (depth <= 0)
        {
            return Chance(4) ? new NegatedPropertySet(Iris(0, 2), Iris(0, 2)) : new PredicatePath(Iri());
        }

        return _random.Next(7) switch
        {
            0 => new InversePath(Path(depth - 1)),
            1 => new SequencePath(Path(depth - 1), Path(depth - 1)),
            2 => new AlternativePath(Path(depth - 1), Path(depth - 1)),
            3 => new ZeroOrMorePath(Path(depth - 1)),
            4 => new OneOrMorePath(Path(depth - 1)),
            5 => new ZeroOrOnePath(Path(depth - 1)),
            _ => Path(0),
        };
    }

    // --- expressions -------------------------------------------------------

    /// <summary>An expression; at a grouped level, bare variables come from the keys only.</summary>
    private Expression Expression(int depth, List<Variable>? keys, bool aggregates)
    {
        if (depth <= 0 || Chance(3))
        {
            if (Chance(2))
            {
                return Constant();
            }

            if (keys is null)
            {
                return new VariableExpression(PoolVariable());
            }

            return keys.Count > 0 ? new VariableExpression(keys[_random.Next(keys.Count)]) : Constant();
        }

        switch (_random.Next(9))
        {
            case 0:
                return new UnaryExpression((UnaryOperator)_random.Next(3), Expression(depth - 1, keys, aggregates));
            case 1:
            case 2:
                return new BinaryExpression((BinaryOperator)_random.Next(12), Expression(depth - 1, keys, aggregates), Expression(depth - 1, keys, aggregates));
            case 3:
                return BuiltIn(depth - 1, keys, aggregates);
            case 4:
                return new CustomFunctionCall(Iri(), Expressions(0, 2, depth - 1, keys, aggregates));
            case 5:
                return new ExistsExpression(Pattern(1), Chance(2));
            case 6 when aggregates:
                return Aggregate(keys);
            case 7:
                return new FunctionCall(BuiltInFunction.Triple, AlgebraList.Of(Expression(depth - 1, keys, aggregates), Expression(depth - 1, keys, aggregates), Expression(depth - 1, keys, aggregates)));
            default:
                return new FunctionCall(Chance(2) ? BuiltInFunction.In : BuiltInFunction.NotIn, Expressions(1, 3, depth - 1, keys, aggregates));
        }
    }

    private static readonly (BuiltInFunction Function, int Min, int Max)[] Arities =
    [
        (BuiltInFunction.Str, 1, 1), (BuiltInFunction.Lang, 1, 1), (BuiltInFunction.LangMatches, 2, 2), (BuiltInFunction.LangDir, 1, 1),
        (BuiltInFunction.Datatype, 1, 1), (BuiltInFunction.Iri, 1, 1), (BuiltInFunction.BNode, 0, 1), (BuiltInFunction.Rand, 0, 0),
        (BuiltInFunction.Abs, 1, 1), (BuiltInFunction.Ceil, 1, 1), (BuiltInFunction.Floor, 1, 1), (BuiltInFunction.Round, 1, 1),
        (BuiltInFunction.Concat, 0, 3), (BuiltInFunction.Substr, 2, 3), (BuiltInFunction.StrLen, 1, 1), (BuiltInFunction.Replace, 3, 4),
        (BuiltInFunction.UCase, 1, 1), (BuiltInFunction.LCase, 1, 1), (BuiltInFunction.EncodeForUri, 1, 1), (BuiltInFunction.Contains, 2, 2),
        (BuiltInFunction.StrStarts, 2, 2), (BuiltInFunction.StrEnds, 2, 2), (BuiltInFunction.StrBefore, 2, 2), (BuiltInFunction.StrAfter, 2, 2),
        (BuiltInFunction.Year, 1, 1), (BuiltInFunction.Month, 1, 1), (BuiltInFunction.Day, 1, 1), (BuiltInFunction.Hours, 1, 1),
        (BuiltInFunction.Minutes, 1, 1), (BuiltInFunction.Seconds, 1, 1), (BuiltInFunction.Timezone, 1, 1), (BuiltInFunction.Tz, 1, 1),
        (BuiltInFunction.Now, 0, 0), (BuiltInFunction.Uuid, 0, 0), (BuiltInFunction.StrUuid, 0, 0), (BuiltInFunction.Md5, 1, 1),
        (BuiltInFunction.Sha1, 1, 1), (BuiltInFunction.Sha256, 1, 1), (BuiltInFunction.Sha384, 1, 1), (BuiltInFunction.Sha512, 1, 1),
        (BuiltInFunction.Coalesce, 0, 3), (BuiltInFunction.If, 3, 3), (BuiltInFunction.StrLang, 2, 2), (BuiltInFunction.StrLangDir, 3, 3),
        (BuiltInFunction.StrDt, 2, 2), (BuiltInFunction.SameTerm, 2, 2), (BuiltInFunction.IsIri, 1, 1), (BuiltInFunction.IsBlank, 1, 1),
        (BuiltInFunction.IsLiteral, 1, 1), (BuiltInFunction.IsNumeric, 1, 1), (BuiltInFunction.HasLang, 1, 1), (BuiltInFunction.HasLangDir, 1, 1),
        (BuiltInFunction.Regex, 2, 3), (BuiltInFunction.IsTriple, 1, 1), (BuiltInFunction.Subject, 1, 1), (BuiltInFunction.Predicate, 1, 1),
        (BuiltInFunction.Object, 1, 1),
    ];

    private FunctionCall BuiltIn(int depth, List<Variable>? keys, bool aggregates)
    {
        if (Chance(8))
        {
            return new FunctionCall(BuiltInFunction.Bound, AlgebraList.Of<Expression>(new VariableExpression(PoolVariable())));
        }

        (BuiltInFunction function, int min, int max) = Arities[_random.Next(Arities.Length)];
        return new FunctionCall(function, Expressions(min, max, depth, keys, aggregates));
    }

    private AlgebraList<Expression> Expressions(int min, int max, int depth, List<Variable>? keys, bool aggregates)
    {
        List<Expression> expressions = [];

        for (int i = _random.Next(min, max + 1); i > 0; i--)
        {
            expressions.Add(Expression(depth, keys, aggregates));
        }

        return AlgebraList.From(expressions);
    }

    private AggregateExpression Aggregate(List<Variable>? keys)
    {
        _ = keys;
        AggregateFunction function = (AggregateFunction)_random.Next(8);
        bool distinct = function == AggregateFunction.Custom || Chance(2);
        Expression? argument = function == AggregateFunction.Count && Chance(2) ? null : Expression(1, null, aggregates: false);
        string? separator = function == AggregateFunction.GroupConcat && Chance(2) ? "; " : null;
        RdfTerm? custom = function == AggregateFunction.Custom ? Iri() : null;
        return new AggregateExpression(function, argument, distinct, separator, custom);
    }

    private ConstantExpression Constant() => new ConstantExpression(Chance(6) ? ExpressionTripleTerm() : Term());

    private RdfTerm ExpressionTripleTerm() => RdfTerm.TripleTerm(Iri(), Iri(), Chance(4) ? ExpressionTripleTerm() : Term());

    // --- terms -------------------------------------------------------------

    private RdfTerm Term() => Chance(2) ? Iri() : Literal();

    private RdfTerm Iri() => RdfTerm.Iri(Encoding.UTF8.GetBytes("http://example.org/" + Pick(["a", "b", "c", "p", "q", "ns#x", "path/to?q=1"])));

    /// <summary>A dataset clause names at least one graph; an empty one has no text.</summary>
    private DatasetSpec Dataset()
    {
        AlgebraList<RdfTerm> defaults = Iris(0, 2);
        AlgebraList<RdfTerm> named = defaults.IsEmpty ? Iris(1, 2) : Iris(0, 2);
        return new DatasetSpec(defaults, named);
    }

    private AlgebraList<RdfTerm> Iris(int min, int max)
    {
        List<RdfTerm> iris = [];

        for (int i = _random.Next(min, max + 1); i > 0; i--)
        {
            iris.Add(Iri());
        }

        return AlgebraList.From(iris);
    }

    private RdfTerm Literal()
    {
        switch (_random.Next(10))
        {
            case 0:
                return RdfTerm.Literal(Encoding.UTF8.GetBytes(Pick(["1", "-2", "+3", "007", "0"])), XsdInteger);
            case 1:
                return RdfTerm.Literal(Encoding.UTF8.GetBytes(Pick(["1.5", "-.5", "+0.25", "1.", "abc"])), XsdDecimal);
            case 2:
                return RdfTerm.Literal(Encoding.UTF8.GetBytes(Pick(["1e0", "-1.5E+3", ".5e-2", "1.e1", "1.0", "NaN"])), XsdDouble);
            case 3:
                return RdfTerm.Literal(Encoding.UTF8.GetBytes(Pick(["true", "false", "TRUE", "1"])), XsdBoolean);
            case 4:
                return RdfTerm.Literal("2026-09-24"u8, XsdDate);
            case 5:
                return RdfTerm.Literal(Encoding.UTF8.GetBytes(Pick(["chat", "Hello, world", "naïve", "日本語"])), Encoding.UTF8.GetBytes(Pick(["en", "en-GB", "fr"])));
            case 6:
                return RdfTerm.Literal("مرحبا"u8, "ar"u8, Chance(2) ? TextDirection.RightToLeft : TextDirection.LeftToRight);
            default:
                return RdfTerm.Literal(Encoding.UTF8.GetBytes(Pick(["", "plain", "with \"quotes\"", "back\\slash", "line\nbreak", "tab\tand\rreturn", "'single'", "\u0001control", "😀"])));
        }
    }

    // --- updates -----------------------------------------------------------

    private UpdateOperation Operation()
    {
        switch (_random.Next(11))
        {
            case 0:
                return new InsertData(Quads(0, 3, new TripleMode(false, true, true), variableGraph: false));
            case 1:
                return new DeleteData(Quads(0, 3, new TripleMode(false, false, true), variableGraph: false));
            case 2:
                return new DeleteWhere(Quads(0, 3, new TripleMode(true, false, true), variableGraph: true));
            case 3:
            case 4:
                RdfTerm? with = Chance(3) ? Iri() : null;
                AlgebraList<QuadPattern> delete = Chance(2) ? Quads(0, 2, new TripleMode(true, false, true), variableGraph: true) : default;
                AlgebraList<QuadPattern> insert = Chance(2) ? Quads(0, 2, new TripleMode(true, true, true), variableGraph: true) : default;
                DatasetSpec? @using = Chance(3) ? Dataset() : null;
                return new Modify(with, delete, insert, @using, Pattern(_budget));
            case 5:
                return new Load(Iri(), Chance(2) ? Iri() : null, Chance(2));
            case 6:
                return new Clear(Target(), Chance(2));
            case 7:
                return new Drop(Target(), Chance(2));
            case 8:
                return new Create(Iri(), Chance(2));
            case 9:
                return new Add(GraphOrDefault(), GraphOrDefault(), Chance(2));
            default:
                return Chance(2) ? new Move(GraphOrDefault(), GraphOrDefault(), Chance(2)) : new Copy(GraphOrDefault(), GraphOrDefault(), Chance(2));
        }
    }

    private GraphTarget Target() => _random.Next(4) switch
    {
        0 => GraphTarget.Default,
        1 => GraphTarget.Named,
        2 => GraphTarget.All,
        _ => GraphTarget.Of(Iri()),
    };

    private GraphOrDefault GraphOrDefault() => Chance(3) ? Algebra.GraphOrDefault.Default : Algebra.GraphOrDefault.Of(Iri());

    private AlgebraList<QuadPattern> Quads(int min, int max, TripleMode mode, bool variableGraph)
    {
        List<QuadPattern> quads = [];
        PatternTerm? graph = null;

        for (int i = _random.Next(min, max + 1); i > 0; i--)
        {
            if (Chance(2))
            {
                graph = _random.Next(3) switch
                {
                    0 => null,
                    1 when variableGraph => new VariablePattern(PoolVariable()),
                    _ => new TermPattern(Iri()),
                };
            }

            quads.Add(new QuadPattern(Node(mode), Predicate(mode), Node(mode), graph));
        }

        return AlgebraList.From(quads);
    }

    // --- scope and names ---------------------------------------------------

    private static List<Variable> Scope(QueryPattern pattern)
    {
        List<Variable> scope = [];
        Collect(pattern, scope);
        return scope;
    }

    private static void Collect(QueryPattern pattern, List<Variable> scope)
    {
        switch (pattern)
        {
            case Bgp bgp:
                foreach (TriplePattern triple in bgp.Triples)
                {
                    Collect(triple.Subject, scope);
                    Collect(triple.Predicate, scope);
                    Collect(triple.Object, scope);
                }

                break;
            case PathPattern path:
                Collect(path.Subject, scope);
                Collect(path.Object, scope);
                break;
            case Join join:
                Collect(join.Left, scope);
                Collect(join.Right, scope);
                break;
            case LeftJoin leftJoin:
                Collect(leftJoin.Left, scope);
                Collect(leftJoin.Right, scope);
                break;
            case Union union:
                Collect(union.Left, scope);
                Collect(union.Right, scope);
                break;
            case Filter filter:
                Collect(filter.Inner, scope);
                break;
            case Graph graph:
                Collect(graph.Name, scope);
                Collect(graph.Inner, scope);
                break;
            case Service service:
                Collect(service.Name, scope);
                Collect(service.Inner, scope);
                break;
            case Extend extend:
                Collect(extend.Inner, scope);
                Add(scope, extend.Variable);
                break;
            case Minus minus:
                Collect(minus.Left, scope);
                break;
            case Values values:
                foreach (Variable variable in values.Variables)
                {
                    Add(scope, variable);
                }

                break;
            case Group group:
                foreach (GroupKey key in group.Keys)
                {
                    if (key.Variable is Variable named)
                    {
                        Add(scope, named);
                    }
                }

                break;
            case Project project:
                foreach (Variable variable in project.Variables)
                {
                    Add(scope, variable);
                }

                break;
            case OrderBy orderBy:
                Collect(orderBy.Inner, scope);
                break;
            case Distinct distinct:
                Collect(distinct.Inner, scope);
                break;
            case Reduced reduced:
                Collect(reduced.Inner, scope);
                break;
            case Slice slice:
                Collect(slice.Inner, scope);
                break;
            default:
                break;
        }
    }

    private static void Collect(PatternTerm term, List<Variable> scope)
    {
        switch (term)
        {
            case VariablePattern variable:
                Add(scope, variable.Variable);
                break;
            case TripleTermPattern triple:
                Collect(triple.Subject, scope);
                Collect(triple.Predicate, scope);
                Collect(triple.Object, scope);
                break;
            default:
                break;
        }
    }

    private static void Add(List<Variable> scope, Variable variable)
    {
        if (!scope.Contains(variable))
        {
            scope.Add(variable);
        }
    }

    private Variable PoolVariable() => new(Pool[_random.Next(Pool.Length)]);

    private Variable Fresh() => new("v" + (_fresh++).ToString(CultureInfo.InvariantCulture));

    private bool Chance(int oneIn) => _random.Next(oneIn) == 0;

    private string Pick(string[] options) => options[_random.Next(options.Length)];
}
