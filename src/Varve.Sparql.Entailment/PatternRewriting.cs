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
    private readonly Dictionary<RdfTerm, Branch> _containerMembership = new(RdfTerm.Comparer);

    internal PatternRewriting(Schema schema)
        : this(schema, schema.Branches)
    {
    }

    internal PatternRewriting(Schema schema, List<Branch> branches)
    {
        _schema = schema;
        _branches = branches;
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
            if (Unify(branch, query, names, blank, own, _schema.Rules) is { } pattern)
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
    private static Project? Unify(Branch branch, PatternTerm[] query, Variable?[] names, bool[] blank, List<Variable> own, RuleSet rules)
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

        for (int i = 0; i < 3; i++)
        {
            if (query[i] is not TermPattern && query[i] is not TripleTermPattern)
            {
                continue;
            }

            RdfTerm value = query[i] is TermPattern tp ? tp.Term : GroundTerm((TripleTermPattern)query[i]);
            Position at = branch.Out[i];
            int? col = at.IsRole ? roleColumn[at.Index] : at.Index;
            if (col is { } c)
            {
                rows = Keep(rows, c, value);
            }

            if (at.IsRole)
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
        // RDF and RDFS, to xsd:string is bound only where the active graph
        // names that term. The rows that need it are guarded, per term set.
        List<int> bindingColumns = [];
        for (int i = 0; i < 3; i++)
        {
            Position at = branch.Out[i];
            if (names[i] is not null && (at.IsRole ? roleColumn[at.Index] : at.Index) is { } c)
            {
                bindingColumns.Add(c);
            }
        }

        Dictionary<string, (List<RdfTerm> Terms, List<RdfTerm[]> Rows)> guarded = [];
        foreach (RdfTerm[] row in rows)
        {
            List<RdfTerm> terms = [];
            foreach (int c in bindingColumns)
            {
                if (Restricted(row[c], rules) && !terms.Contains(row[c]))
                {
                    terms.Add(row[c]);
                }
            }

            string key = string.Join(" ", terms.ConvertAll(t => Encoding.UTF8.GetString(t.Lexical)));
            if (!guarded.TryGetValue(key, out (List<RdfTerm> Terms, List<RdfTerm[]> Rows) group))
            {
                guarded[key] = group = (terms, []);
            }

            group.Rows.Add(row);
        }

        bool anyGuard = false;
        foreach ((List<RdfTerm> terms, List<RdfTerm[]> _) in guarded.Values)
        {
            anyGuard |= terms.Count > 0;
        }

        if (anyGuard)
        {
            QueryPattern? union = null;
            foreach ((List<RdfTerm> terms, List<RdfTerm[]> part) in guarded.Values)
            {
                if (Build(branch, part, names, blank, own, column, roleColumn, asserted) is not { } built)
                {
                    continue;
                }

                QueryPattern one = built;
                foreach (RdfTerm term in terms)
                {
                    one = new Filter(new ExistsExpression(Occurs(term), false), one);
                }

                union = union is null ? one : new Union(union, one);
            }

            return union is null ? null : new Project(union, AlgebraList.From(own));
        }

        return Build(branch, rows, names, blank, own, column, roleColumn, asserted);
    }

    // Whether a term is in the closure without the graph naming it.
    private static bool Restricted(RdfTerm term, RuleSet rules) =>
        Vocabulary.IsContainerMembership(term) || (term.Equals(Vocabulary.XsdString) && !rules.Recognised.Contains(Vocabulary.Integer));

    // The active graph names the term in some position.
    private static Union Occurs(RdfTerm term)
    {
        TermPattern t = new(term);
        VariablePattern a = new(new Variable(Prefix + "o1"));
        VariablePattern b = new(new Variable(Prefix + "o2"));
        return new Union(
            new Union(new Bgp(AlgebraList.Of(new TriplePattern(t, a, b))), new Bgp(AlgebraList.Of(new TriplePattern(a, t, b)))),
            new Bgp(AlgebraList.Of(new TriplePattern(a, b, t))));
    }

    private static Project? Build(Branch branch, List<RdfTerm[]> rows, Variable?[] names, bool[] blank, List<Variable> own, Variable[] column, int?[] roleColumn, PatternTerm[] asserted)
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

            if (bound.TryGetValue(name, out Expression? earlier))
            {
                equalities.Add(new FunctionCall(BuiltInFunction.SameTerm, AlgebraList.Of(earlier, source)));
            }
            else
            {
                bound[name] = source;
            }
        }

        QueryPattern? core = Asserted(branch.Family, asserted);
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
        }
        else
        {
            // Point 3: a fact that exists only if some asserted triple of the shape does.
            pattern = new Filter(new ExistsExpression(core, false), table);
        }

        foreach ((Variable name, Expression source) in bound)
        {
            pattern = new Extend(pattern, name, source);
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
            Family.LiteralObject => LiteralCondition(bgp, asserted[2]),
            _ => bgp,
        };
    }

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
