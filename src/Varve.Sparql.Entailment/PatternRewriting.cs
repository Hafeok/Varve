// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Varve.Sparql.Entailment.Closure;

namespace Varve.Sparql.Entailment;

/// <summary>
/// Rewrites one basic graph pattern under one compiled schema (ADR 0193
/// points 3, 4 and 6): each triple pattern becomes the union of the branches
/// that can entail it, unified with it; the join of those is <c>Distinct</c>
/// over its variables and its blank nodes, then projected to its variables.
/// </summary>
[Contract(typeof(RdfsRdfAndDByRewriting.OneAssertedPremisePerEntailment), Role = "a basic graph pattern rewritten under a schema")]
internal sealed class PatternRewriting
{
    private const string Prefix = "entailment:";
    private static readonly ImmutableArray<Variable> Roles = [new(Prefix + "a"), new(Prefix + "p"), new(Prefix + "b")];

    private readonly List<Branch> _branches;
    private readonly Schema _schema;
    private readonly Func<RdfTerm, bool> _occurs;
    private readonly Func<RdfTerm?, IReadOnlyList<RdfTerm>>? _distinct;
    private readonly Dictionary<RdfTerm, Branch> _containerMembership = new(RdfTerm.Comparer);

    /// <summary>
    /// The rewriting under <paramref name="schema"/>, where
    /// <paramref name="occurs"/> says whether the scoping graph names a term:
    /// an existence probe, memoised for the request (ADR 0193 point 4); and
    /// <paramref name="distinct"/>, when the scoping graph is one set of
    /// graphs, gives its distinct predicates (for none) or the distinct
    /// objects of a predicate, by a seek past each.
    /// </summary>
    internal PatternRewriting(Schema schema, Func<RdfTerm, bool> occurs, Func<RdfTerm?, IReadOnlyList<RdfTerm>>? distinct)
    {
        _schema = schema;
        _branches = schema.Branches;
        _occurs = occurs;
        _distinct = distinct;
    }

    /// <summary>
    /// The terms C2 asks the scoping graph about: those of the tables a
    /// variable can be bound to without the graph naming them, and the
    /// <c>rdf:_n</c> the query names.
    /// </summary>
    internal IEnumerable<RdfTerm> Candidates(IEnumerable<RdfTerm> named)
    {
        HashSet<RdfTerm> seen = new(RdfTerm.Comparer);
        foreach (Branch branch in _branches)
        {
            foreach (RdfTerm[] row in branch.Rows)
            {
                foreach (RdfTerm term in row)
                {
                    if (Restricted(term, _schema.Rules) && seen.Add(term))
                    {
                        yield return term;
                    }
                }
            }
        }

        foreach (RdfTerm term in named)
        {
            if (seen.Add(term))
            {
                yield return term;
            }
        }
    }

    /// <summary>The rewritten form of <paramref name="bgp"/>.</summary>
    internal QueryPattern Rewrite(Bgp bgp)
    {
        if (bgp.Triples.IsEmpty)
        {
            return bgp;
        }

        List<Variable> visible = [];
        List<Variable> all = [];
        QueryPattern? joined = null;
        foreach (TriplePattern triple in bgp.Triples)
        {
            QueryPattern rewritten = RewriteTriple(triple, visible, all);
            joined = joined is null ? rewritten : new Join(joined, rewritten);
        }

        // Point 6: once per distinct solution and blank-node mapping.
        return new Project(new Distinct(new Project(joined!, AlgebraList.From(all))), AlgebraList.From(visible));
    }

    private QueryPattern RewriteTriple(TriplePattern triple, List<Variable> visible, List<Variable> all)
    {
        PatternTerm[] query = [triple.Subject, triple.Predicate, triple.Object];
        Variable?[] names = new Variable?[3];
        bool[] blank = new bool[3];
        List<Variable> own = [];
        for (int i = 0; i < 3; i++)
        {
            switch (query[i])
            {
                case VariablePattern v:
                    names[i] = v.Variable;
                    Note(v.Variable, visible);
                    break;
                case BlankNodePattern b:
                    names[i] = new Variable(Prefix + "bnode:" + b.Label);
                    blank[i] = true;
                    break;
                case TripleTermPattern t when !IsGround(t):
                    // RDF 1.2 triple terms with variables are outside the
                    // RDF 1.1 regimes: the pattern is matched as written.
                    foreach (Variable v in VariablesOf(t, visible))
                    {
                        Note(v, all);
                    }

                    return new Bgp(AlgebraList.Of(triple));
            }

            if (names[i] is { } name)
            {
                Note(name, all);
                Note(name, own);
            }
        }

        QueryPattern? union = null;
        foreach (Branch branch in Candidates(query))
        {
            if (Unify(branch, query, names, blank, own, _schema.Rules, _occurs, _distinct) is { } pattern)
            {
                union = union is null ? pattern : new Union(union, pattern);
            }
        }

        // Nothing entails it: an empty table of its variables.
        return union ?? new Values(AlgebraList.From(own), AlgebraList.Empty<AlgebraList<RdfTerm?>>());
    }

    // The compiled branches, and for each rdf:_n the query names, its axioms'
    // consequences: a ground pattern needs no binding, so C2 does not restrict
    // it, and rdf:_7 is a property on any graph (Entailment Regimes §3.4).
    private IEnumerable<Branch> Candidates(PatternTerm[] query)
    {
        foreach (Branch branch in _branches)
        {
            yield return branch;
        }

        foreach (PatternTerm term in query)
        {
            if (term is TermPattern { Term: var named } && Vocabulary.IsContainerMembership(named))
            {
                if (!_containerMembership.TryGetValue(named, out Branch? branch))
                {
                    _containerMembership[named] = branch = Shapes.ContainerMembershipConstant(_schema, named);
                }

                yield return branch;
            }
        }
    }

    // One branch unified with the query triple: constants filter the table or
    // bind the asserted triple, variables are bound from the outputs, a
    // repeated variable is an equality. Null when nothing can match.
    private static Project? Unify(Branch branch, PatternTerm[] query, Variable?[] names, bool[] blank, List<Variable> own, RuleSet rules, Func<RdfTerm, bool> occurs, Func<RdfTerm?, IReadOnlyList<RdfTerm>>? distinct)
    {
        int columns = branch.Parameters + branch.Slots;
        Variable[] column = new Variable[columns];
        for (int c = 0; c < columns; c++)
        {
            column[c] = new Variable(Prefix + "c" + c.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // Roles a column carries too: a known predicate's or class's parameter.
        int?[] roleColumn = RoleColumns(branch.Family);
        PatternTerm[] asserted = new PatternTerm[3];
        for (int r = 0; r < 3; r++)
        {
            asserted[r] = new VariablePattern(roleColumn[r] is { } c ? column[c] : Roles[r]);
        }

        List<RdfTerm[]> rows = branch.Rows;

        List<(int Role, RdfTerm Canonical)> valueConstraints = [];
        for (int i = 0; i < 3; i++)
        {
            if (query[i] is not TermPattern && query[i] is not TripleTermPattern)
            {
                continue;
            }

            RdfTerm value = query[i] is TermPattern tp ? tp.Term : GroundTerm((TripleTermPattern)query[i]);

            Position at = branch.Out[i];
            int? col = at.IsRole ? roleColumn[at.Index] : at.Index;
            // Point 7: under D a literal constant matches by value.
            bool byValue = ByValue(rules) && value.Kind == RdfTermKind.Literal;
            if (col is { } c)
            {
                rows = byValue ? KeepByValue(rows, c, value) : Keep(rows, c, value);
            }

            if (at.IsRole && byValue && roleColumn[at.Index] is null)
            {
                valueConstraints.Add((at.Index, LiteralValues.Canonical(value)));
            }
            else if (at.IsRole)
            {
                // Two positions of one role take one term.
                if (asserted[at.Index] is TermPattern { Term: var earlier } && !earlier.Equals(value))
                {
                    return null;
                }

                asserted[at.Index] = new TermPattern(value);
            }
        }

        if (rows.Count == 0 && branch.Rows.Count > 0)
        {
            return null;
        }

        // C2, and Varve's reading of σ (ADR 0193 point 4): a variable or a
        // blank node of the query bound from a column to an rdf:_n or, under
        // RDF and RDFS, to xsd:string is bound only where the scoping graph
        // names that term, which a probe answered when the query was rewritten.
        List<int> bindingColumns = [];
        for (int i = 0; i < 3; i++)
        {
            Position at = branch.Out[i];
            if (names[i] is not null && (at.IsRole ? roleColumn[at.Index] : at.Index) is { } c)
            {
                bindingColumns.Add(c);
            }
        }

        if (bindingColumns.Count > 0)
        {
            List<RdfTerm[]> named = [];
            foreach (RdfTerm[] row in rows)
            {
                if (bindingColumns.TrueForAll(c => !Restricted(row[c], rules) || occurs(row[c])))
                {
                    named.Add(row);
                }
            }

            if (named.Count == 0 && rows.Count > 0)
            {
                return null;
            }

            rows = named;
        }

        return Build(branch, rows, names, blank, own, column, roleColumn, asserted, valueConstraints, ByValue(rules), distinct);
    }

    private static bool ByValue(RuleSet rules) => rules.ByValue;

    private static CustomFunctionCall Canonicalised(Expression expression) =>
        new(LiteralValues.FunctionIri, AlgebraList.Of(expression));

    private static List<RdfTerm[]> KeepByValue(List<RdfTerm[]> rows, int column, RdfTerm value)
    {
        RdfTerm canonical = LiteralValues.Canonical(value);
        List<RdfTerm[]> kept = [];
        foreach (RdfTerm[] row in rows)
        {
            if (LiteralValues.Canonical(row[column]).Equals(canonical))
            {
                kept.Add(row);
            }
        }

        return kept;
    }

    // Whether a term is in the closure without the graph naming it.
    private static bool Restricted(RdfTerm term, RuleSet rules) =>
        Vocabulary.IsContainerMembership(term) || (term.Equals(Vocabulary.XsdString) && !rules.ByValue);

    private static Project? Build(Branch branch, List<RdfTerm[]> rows, Variable?[] names, bool[] blank, List<Variable> own, Variable[] column, int?[] roleColumn, PatternTerm[] asserted, List<(int Role, RdfTerm Canonical)> valueConstraints, bool byValue, Func<RdfTerm?, IReadOnlyList<RdfTerm>>? distinct)
    {
        int columns = column.Length;

        // Each query variable's source, and the equalities a repeat makes.
        Dictionary<Variable, Expression> bound = [];
        List<Expression> equalities = [];
        for (int i = 0; i < 3; i++)
        {
            if (names[i] is not { } name)
            {
                continue;
            }

            Expression source = Source(branch.Out[i], asserted, column, roleColumn);
            if (byValue)
            {
                source = Canonicalised(source);
            }

            if (bound.TryGetValue(name, out Expression? earlier))
            {
                equalities.Add(new FunctionCall(BuiltInFunction.SameTerm, AlgebraList.Of(earlier, source)));
            }
            else
            {
                bound[name] = source;
            }
        }

        bool literalByValue = byValue && branch.Family == Family.LiteralObject;
        QueryPattern? core = literalByValue ? new Bgp(AlgebraList.Of(new TriplePattern(asserted[0], asserted[1], asserted[2]))) : Asserted(branch.Family, asserted);

        // A fact read off the predicate alone, or off a type-like triple's
        // class alone, needs each distinct one once, not every triple: the
        // scoping graph's seek gives them (ADR 0193).
        if (distinct is not null && core is not null && DistinctOnly(branch, asserted, rows, distinct) is { } seeked)
        {
            core = Restrict(branch.Family, asserted, seeked);
        }

        // Under D the literal family's parameter is a datatype the literal's
        // value is in, tested once the table has bound it.
        Expression? late = literalByValue
            ? new BinaryExpression(
                BinaryOperator.And,
                new FunctionCall(BuiltInFunction.IsLiteral, AlgebraList.Of(Term(asserted[2]))),
                new CustomFunctionCall(LiteralValues.HasValueTypeIri, AlgebraList.Of<Expression>(Term(asserted[2]), new VariableExpression(column[0]))))
            : null;
        QueryPattern table = new Values(
            AlgebraList.From(column),
            AlgebraList.From(Rows(rows)));
        QueryPattern pattern;
        bool readsRoles = false;
        foreach (Position position in branch.Out)
        {
            readsRoles |= position.IsRole;
        }

        if (core is null)
        {
            pattern = table;
        }
        else if (readsRoles)
        {
            pattern = columns == 0 ? core : new Join(table, core);
            if (late is not null)
            {
                pattern = new Filter(late, pattern);
            }
        }
        else
        {
            // Point 3: a fact that exists only if some asserted triple of the shape does.
            pattern = new Filter(new ExistsExpression(late is null ? core : new Filter(late, core), false), table);
        }

        foreach ((Variable name, Expression source) in bound)
        {
            pattern = new Extend(pattern, name, source);
        }

        foreach ((int role, RdfTerm canonical) in valueConstraints)
        {
            equalities.Add(new FunctionCall(
                BuiltInFunction.SameTerm,
                AlgebraList.Of<Expression>(Canonicalised(new VariableExpression(Roles[role])), new ConstantExpression(canonical))));
        }

        foreach (Expression equality in equalities)
        {
            pattern = new Filter(equality, pattern);
        }

        // Point 4: no literal in a variable's subject position.
        if (names[0] is { } subject && !blank[0])
        {
            pattern = new Filter(
                new UnaryExpression(UnaryOperator.Not, new FunctionCall(BuiltInFunction.IsLiteral, AlgebraList.Of<Expression>(new VariableExpression(subject)))),
                pattern);
        }

        return new Project(pattern, AlgebraList.From(own));
    }

    // The asserted triple a family reads, with its condition; null for Σ.
    private static QueryPattern? Asserted(Family family, PatternTerm[] asserted)
    {
        Bgp bgp = new(AlgebraList.Of(new TriplePattern(asserted[0], asserted[1], asserted[2])));
        return family switch
        {
            Family.Schema => null,
            Family.ContainerMembershipPredicate => Condition(bgp, IsContainerMembership(asserted[1])),
            Family.ContainerMembershipSubject => Condition(bgp, IsContainerMembership(asserted[0])),
            Family.ContainerMembershipObject => Condition(bgp, IsContainerMembership(asserted[2])),
            Family.ContainerMembershipClass => Condition(bgp, IsContainerMembership(asserted[2])),
            Family.LiteralObject => LiteralCondition(bgp, asserted[2]),
            _ => bgp,
        };
    }

    // The distinct terms that stand for the asserted triple when the fact
    // reads one role of it and the others are free: the predicates, or the
    // classes of each type-like predicate the table names.
    private static Values? DistinctOnly(Branch branch, PatternTerm[] asserted, List<RdfTerm[]> rows, Func<RdfTerm?, IReadOnlyList<RdfTerm>> distinct)
    {
        int read = -1;
        foreach (Position at in branch.Out)
        {
            if (at.IsRole)
            {
                if (read >= 0 && read != at.Index)
                {
                    return null;
                }

                read = at.Index;
            }
        }

        bool Free(int role) => asserted[role] is VariablePattern { Variable: var v } && v.Equals(Roles[role]);

        switch (branch.Family)
        {
            case Family.AnyPredicate or Family.ContainerMembershipPredicate when read == 1 && Free(0) && Free(1) && Free(2):
                return new Values(AlgebraList.Of(Roles[1]), AlgebraList.From(Column(distinct(null))));
            case Family.AnyClass or Family.ContainerMembershipClass when read == 2 && Free(0) && Free(2):
                if (asserted[1] is TermPattern { Term: var type })
                {
                    return new Values(AlgebraList.Of(Roles[2]), AlgebraList.From(Column(distinct(type))));
                }

                if (asserted[1] is not VariablePattern { Variable: var typeColumn })
                {
                    return null;
                }

                List<AlgebraList<RdfTerm?>> pairs = [];
                HashSet<RdfTerm> types = new(RdfTerm.Comparer);
                foreach (RdfTerm[] row in rows)
                {
                    if (types.Add(row[0]))
                    {
                        foreach (RdfTerm @class in distinct(row[0]))
                        {
                            pairs.Add(AlgebraList.Of<RdfTerm?>(row[0], @class));
                        }
                    }
                }

                return new Values(AlgebraList.Of(typeColumn, Roles[2]), AlgebraList.From(pairs));
            default:
                return null;
        }

        static IEnumerable<AlgebraList<RdfTerm?>> Column(IReadOnlyList<RdfTerm> terms)
        {
            foreach (RdfTerm term in terms)
            {
                yield return AlgebraList.Of<RdfTerm?>(term);
            }
        }
    }

    // The family's condition on the asserted triple, on what stands for it.
    private static QueryPattern Restrict(Family family, PatternTerm[] asserted, QueryPattern inner) => family switch
    {
        Family.ContainerMembershipPredicate => new Filter(IsContainerMembership(asserted[1]), inner),
        Family.ContainerMembershipClass => new Filter(IsContainerMembership(asserted[2]), inner),
        _ => inner,
    };

    // The literal family's table joins on the object's datatype.
    private static Extend LiteralCondition(Bgp bgp, PatternTerm @object)
    {
        Expression value = Term(@object);
        QueryPattern literal = new Filter(new FunctionCall(BuiltInFunction.IsLiteral, AlgebraList.Of(value)), bgp);
        return new Extend(literal, new Variable(Prefix + "c0"), new FunctionCall(BuiltInFunction.Datatype, AlgebraList.Of(value)));
    }

    private static Filter Condition(Bgp bgp, Expression condition) => new Filter(condition, bgp);

    private static BinaryExpression IsContainerMembership(PatternTerm term) =>
        new BinaryExpression(
            BinaryOperator.And,
            new FunctionCall(BuiltInFunction.IsIri, AlgebraList.Of(Term(term))),
            new FunctionCall(
                BuiltInFunction.Regex,
                AlgebraList.Of<Expression>(
                    new FunctionCall(BuiltInFunction.Str, AlgebraList.Of(Term(term))),
                    new ConstantExpression(RdfTerm.Literal(Encoding.UTF8.GetBytes(Vocabulary.ContainerMembershipPattern))))));

    private static Expression Term(PatternTerm term) => term switch
    {
        VariablePattern v => new VariableExpression(v.Variable),
        TermPattern t => new ConstantExpression(t.Term),
        _ => throw new InvalidOperationException("An asserted triple holds variables and terms only."),
    };

    private static Expression Source(Position at, PatternTerm[] asserted, Variable[] column, int?[] roleColumn)
    {
        if (!at.IsRole)
        {
            return new VariableExpression(column[at.Index]);
        }

        return asserted[at.Index] switch
        {
            TermPattern t => new ConstantExpression(t.Term),
            _ => roleColumn[at.Index] is { } c ? new VariableExpression(column[c]) : new VariableExpression(Roles[at.Index]),
        };
    }

    // The column a role is joined on, by family: the parameters come first.
    private static int?[] RoleColumns(Family family) => family switch
    {
        Family.KnownPredicate => [null, 0, null],
        Family.KnownClass => [null, 0, 1],
        Family.AnyClass => [null, 0, null],
        Family.ContainerMembershipClass => [null, 0, null],
        _ => [null, null, null],
    };

    private static List<RdfTerm[]> Keep(List<RdfTerm[]> rows, int column, RdfTerm value)
    {
        List<RdfTerm[]> kept = [];
        foreach (RdfTerm[] row in rows)
        {
            if (row[column].Equals(value))
            {
                kept.Add(row);
            }
        }

        return kept;
    }

    private static IEnumerable<AlgebraList<RdfTerm?>> Rows(List<RdfTerm[]> rows)
    {
        foreach (RdfTerm[] row in rows)
        {
            RdfTerm?[] values = new RdfTerm?[row.Length];
            Array.Copy(row, values, row.Length);
            yield return AlgebraList.From<RdfTerm?>(values);
        }
    }

    private static void Note(Variable variable, List<Variable> into)
    {
        if (!into.Contains(variable))
        {
            into.Add(variable);
        }
    }

    private static bool IsGround(TripleTermPattern t) =>
        Ground(t.Subject) && Ground(t.Predicate) && Ground(t.Object);

    private static bool Ground(PatternTerm term) => term switch
    {
        TermPattern => true,
        TripleTermPattern t => IsGround(t),
        _ => false,
    };

    private static RdfTerm GroundTerm(TripleTermPattern t) =>
        RdfTerm.TripleTerm(GroundOf(t.Subject), GroundOf(t.Predicate), GroundOf(t.Object));

    private static RdfTerm GroundOf(PatternTerm term) => term switch
    {
        TermPattern tp => tp.Term,
        TripleTermPattern t => GroundTerm(t),
        _ => throw new InvalidOperationException("Not a ground triple term."),
    };

    private static IEnumerable<Variable> VariablesOf(TripleTermPattern t, List<Variable> visible)
    {
        foreach (PatternTerm term in new[] { t.Subject, t.Predicate, t.Object })
        {
            if (term is VariablePattern v)
            {
                Note(v.Variable, visible);
                yield return v.Variable;
            }
            else if (term is TripleTermPattern inner)
            {
                foreach (Variable nested in VariablesOf(inner, visible))
                {
                    yield return nested;
                }
            }
        }
    }
}
