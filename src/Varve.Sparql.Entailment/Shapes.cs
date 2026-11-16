// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Sparql.Entailment.Closure;

namespace Varve.Sparql.Entailment;

/// <summary>
/// The asserted triples a branch reads, and the condition on them
/// (ADR 0193 point 3). Each family has one representative shape per
/// parameter value, or one generic shape.
/// </summary>
internal enum Family : byte
{
    /// <summary>Σ itself: no asserted triple, its triples as constants.</summary>
    Schema,

    /// <summary>A triple whose predicate Σ knows; the predicate is the parameter.</summary>
    KnownPredicate,

    /// <summary>A type-like triple whose class Σ knows; the predicate and the class are the parameters.</summary>
    KnownClass,

    /// <summary>Any triple, under the facts every triple has.</summary>
    AnyPredicate,

    /// <summary>A triple whose predicate is <c>rdf:_n</c>.</summary>
    ContainerMembershipPredicate,

    /// <summary>A type-like triple, under the facts every class has; the predicate is the parameter.</summary>
    AnyClass,

    /// <summary>A triple whose object is a literal of a recognised datatype; the datatype is the parameter.</summary>
    LiteralObject,

    /// <summary>A triple whose subject is <c>rdf:_n</c>.</summary>
    ContainerMembershipSubject,

    /// <summary>A triple whose object is <c>rdf:_n</c>.</summary>
    ContainerMembershipObject,

    /// <summary>A type-like triple whose class is <c>rdf:_n</c>, which meets its own axioms; the predicate is the parameter.</summary>
    ContainerMembershipClass,
}

/// <summary>Where a branch's output position comes from: a role of the asserted triple, or a column of its table.</summary>
internal readonly record struct Position(bool IsRole, int Index)
{
    internal const int SubjectRole = 0;
    internal const int PredicateRole = 1;
    internal const int ObjectRole = 2;

    internal static Position Role(int role) => new(true, role);

    internal static Position Column(int column) => new(false, column);
}

/// <summary>
/// One compiled branch (ADR 0193 point 3): a family, its table — the
/// parameters first, then the slots — and the three positions of the triple
/// it entails.
/// </summary>
internal sealed class Branch
{
    internal Branch(Family family, int parameters, int slots, Position subject, Position predicate, Position @object)
    {
        Family = family;
        Parameters = parameters;
        Slots = slots;
        Out = [subject, predicate, @object];
    }

    internal Family Family { get; }

    internal int Parameters { get; }

    internal int Slots { get; }

    internal Position[] Out { get; }

    internal List<RdfTerm[]> Rows { get; } = [];
}

/// <summary>
/// Compiles a saturated schema into branches (ADR 0193 point 3): for each
/// shape, the closure of Σ and one representative asserted triple, whose own
/// consequences, read off by role, are the rows of its family's branches.
/// </summary>
[Contract(typeof(RdfsRdfAndDByRewriting.OneAssertedPremisePerEntailment), Role = "the tables a schema compiles to")]
internal static class Shapes
{
    private static readonly RdfTerm RoleSubject = Placeholder("s");
    private static readonly RdfTerm RolePredicate = Placeholder("p");
    private static readonly RdfTerm RoleObject = Placeholder("o");

    /// <summary>The branches of <paramref name="schema"/>, Σ's first.</summary>
    internal static List<Branch> Compile(Schema schema)
    {
        List<Branch> branches = [];
        Branch sigma = new(Family.Schema, 0, 3, Position.Column(0), Position.Column(1), Position.Column(2));
        foreach (Triple t in schema.Closure.All)
        {
            sigma.Rows.Add([t.S, t.P, t.O]);
        }

        branches.Add(sigma);
        Dictionary<string, Branch> grouped = [];

        foreach (RdfTerm predicate in schema.KnownPredicates)
        {
            Collect(schema, grouped, Family.KnownPredicate, [predicate], new Triple(RoleSubject, predicate, RoleObject), predicate, RoleObject);
        }

        foreach (RdfTerm type in schema.TypeLike)
        {
            foreach (RdfTerm @class in schema.KnownClasses)
            {
                Collect(schema, grouped, Family.KnownClass, [type, @class], new Triple(RoleSubject, type, @class), type, @class);
            }

            Collect(schema, grouped, Family.AnyClass, [type], new Triple(RoleSubject, type, RoleObject), type, RoleObject);
            Collect(schema, grouped, Family.ContainerMembershipClass, [type], new Triple(RoleSubject, type, RoleObject), type, RoleObject, containerMembership: RoleObject);
        }

        Collect(schema, grouped, Family.AnyPredicate, [], new Triple(RoleSubject, RolePredicate, RoleObject), RolePredicate, RoleObject);

        foreach (RdfTerm datatype in schema.Rules.Recognised)
        {
            Collect(schema, grouped, Family.LiteralObject, [datatype], new Triple(RoleSubject, RolePredicate, LiteralOf(datatype)), RolePredicate, LiteralOf(datatype));
        }

        // rdf:_n: its own axioms, as a predicate and as a term.
        Collect(schema, grouped, Family.ContainerMembershipPredicate, [], new Triple(RoleSubject, RolePredicate, RoleObject), RolePredicate, RoleObject, containerMembership: RolePredicate);
        Collect(schema, grouped, Family.ContainerMembershipSubject, [], null, RolePredicate, RoleObject, containerMembership: RoleSubject);
        Collect(schema, grouped, Family.ContainerMembershipObject, [], null, RolePredicate, RoleObject, containerMembership: RoleObject);

        branches.AddRange(grouped.Values);
        return branches;
    }

    /// <summary>The consequences of one <c>rdf:_n</c>'s axioms, as constants.</summary>
    internal static Branch ContainerMembershipConstant(Schema schema, RdfTerm term)
    {
        TripleClosure closure = new(schema.Rules, schema.Closure);
        closure.AddContainerMembershipAxioms([term]);
        closure.Close();
        Branch branch = new(Family.Schema, 0, 3, Position.Column(0), Position.Column(1), Position.Column(2));
        foreach (Triple t in closure.Own)
        {
            branch.Rows.Add([t.S, t.P, t.O]);
        }

        return branch;
    }

    // The own consequences of one representative, each read off as roles and
    // slots, joined to the branch of its family and template.
    private static void Collect(
        Schema schema,
        Dictionary<string, Branch> grouped,
        Family family,
        RdfTerm[] parameters,
        Triple? representative,
        RdfTerm predicateRole,
        RdfTerm objectRole,
        RdfTerm? containerMembership = null)
    {
        TripleClosure closure = new(schema.Rules, schema.Closure);
        if (containerMembership is not null)
        {
            foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in Vocabulary.ContainerMembershipAxioms(containerMembership, schema.Rules.Rdfs))
            {
                closure.Add(s, p, o);
            }
        }

        if (representative is { } triple)
        {
            closure.Add(triple);
        }

        closure.Close();
        foreach (Triple fact in closure.Own)
        {
            Position[] positions = new Position[3];
            List<RdfTerm> slots = [];
            RdfTerm[] terms = [fact.S, fact.P, fact.O];
            for (int i = 0; i < 3; i++)
            {
                RdfTerm term = terms[i];
                if (term.Equals(RoleSubject))
                {
                    positions[i] = Position.Role(Position.SubjectRole);
                }
                else if (term.Equals(predicateRole))
                {
                    positions[i] = Position.Role(Position.PredicateRole);
                }
                else if (term.Equals(objectRole))
                {
                    positions[i] = Position.Role(Position.ObjectRole);
                }
                else
                {
                    positions[i] = Position.Column(parameters.Length + slots.Count);
                    slots.Add(term);
                }
            }

            // A fact about a placeholder no position names is never asked for:
            // the placeholder of a family that does not read that role.
            if (Mentions(slots, RoleSubject) || Mentions(slots, RolePredicate) || Mentions(slots, RoleObject))
            {
                continue;
            }

            string key = family + ":" + positions[0] + positions[1] + positions[2];
            if (!grouped.TryGetValue(key, out Branch? branch))
            {
                grouped[key] = branch = new Branch(family, parameters.Length, slots.Count, positions[0], positions[1], positions[2]);
            }

            RdfTerm[] row = new RdfTerm[parameters.Length + slots.Count];
            parameters.CopyTo(row, 0);
            slots.CopyTo(row, parameters.Length);
            branch.Rows.Add(row);
        }
    }

    private static bool Mentions(List<RdfTerm> slots, RdfTerm placeholder)
    {
        foreach (RdfTerm slot in slots)
        {
            if (slot.Equals(placeholder))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The placeholder literal of a recognised datatype.</summary>
    internal static RdfTerm LiteralOf(RdfTerm datatype) =>
        datatype.Lexical.SequenceEqual("http://www.w3.org/1999/02/22-rdf-syntax-ns#langString"u8)
            ? RdfTerm.Literal("\u0001"u8, "x-varve"u8)
            : RdfTerm.Literal("\u0001"u8, datatype);

    private static RdfTerm Placeholder(string role) => RdfTerm.Iri(Encoding.UTF8.GetBytes("urn:x-varve:entailment:role:" + role));
}
