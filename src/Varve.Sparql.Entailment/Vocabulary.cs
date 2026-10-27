// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Varve.Rdf;

namespace Varve.Sparql.Entailment;

/// <summary>The RDF and RDFS vocabularies the rules name (RDF 1.1 Semantics §8, §9).</summary>
internal static class Vocabulary
{
    internal const string RdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    internal const string RdfsNamespace = "http://www.w3.org/2000/01/rdf-schema#";
    internal const string XsdNamespace = "http://www.w3.org/2001/XMLSchema#";

    internal static readonly RdfTerm Type = Rdf("type");
    internal static readonly RdfTerm Property = Rdf("Property");
    internal static readonly RdfTerm LangString = Rdf("langString");
    internal static readonly RdfTerm Statement = Rdf("Statement");
    internal static readonly RdfTerm Subject = Rdf("subject");
    internal static readonly RdfTerm Predicate = Rdf("predicate");
    internal static readonly RdfTerm Object = Rdf("object");
    internal static readonly RdfTerm First = Rdf("first");
    internal static readonly RdfTerm Rest = Rdf("rest");
    internal static readonly RdfTerm Value = Rdf("value");
    internal static readonly RdfTerm Nil = Rdf("nil");
    internal static readonly RdfTerm List = Rdf("List");
    internal static readonly RdfTerm Alt = Rdf("Alt");
    internal static readonly RdfTerm Bag = Rdf("Bag");
    internal static readonly RdfTerm Seq = Rdf("Seq");

    internal static readonly RdfTerm Resource = Rdfs("Resource");
    internal static readonly RdfTerm Class = Rdfs("Class");
    internal static readonly RdfTerm Literal = Rdfs("Literal");
    internal static readonly RdfTerm Datatype = Rdfs("Datatype");
    internal static readonly RdfTerm Container = Rdfs("Container");
    internal static readonly RdfTerm ContainerMembershipProperty = Rdfs("ContainerMembershipProperty");
    internal static readonly RdfTerm SubClassOf = Rdfs("subClassOf");
    internal static readonly RdfTerm SubPropertyOf = Rdfs("subPropertyOf");
    internal static readonly RdfTerm Domain = Rdfs("domain");
    internal static readonly RdfTerm Range = Rdfs("range");
    internal static readonly RdfTerm Member = Rdfs("member");
    internal static readonly RdfTerm Comment = Rdfs("comment");
    internal static readonly RdfTerm Label = Rdfs("label");
    internal static readonly RdfTerm SeeAlso = Rdfs("seeAlso");
    internal static readonly RdfTerm IsDefinedBy = Rdfs("isDefinedBy");

    internal static readonly RdfTerm XsdString = Xsd("string");
    internal static readonly RdfTerm Integer = Xsd("integer");

    /// <summary>The four predicates that make a triple the schema's, before their sub-properties (ADR 0193 point 2).</summary>
    internal static readonly ImmutableArray<RdfTerm> SchemaPredicates = [SubClassOf, SubPropertyOf, Domain, Range];

    /// <summary>
    /// The RDF axiomatic triples of RDF 1.1 Semantics §8.1, less those of
    /// <c>rdf:_n</c>, which <see cref="ContainerMembershipAxioms"/> gives per term.
    /// </summary>
    internal static IEnumerable<(RdfTerm S, RdfTerm P, RdfTerm O)> RdfAxioms()
    {
        foreach (RdfTerm property in new[] { Type, Subject, Predicate, Object, First, Rest, Value })
        {
            yield return (property, Type, Property);
        }

        yield return (Nil, Type, List);
    }

    /// <summary>The RDFS axiomatic triples of RDF 1.1 Semantics §9.1, less those of <c>rdf:_n</c>.</summary>
    internal static IEnumerable<(RdfTerm S, RdfTerm P, RdfTerm O)> RdfsAxioms()
    {
        (RdfTerm, RdfTerm)[] domains =
        [
            (Type, Resource), (Domain, Property), (Range, Property), (SubPropertyOf, Property), (SubClassOf, Class),
            (Subject, Statement), (Predicate, Statement), (Object, Statement), (Member, Resource), (First, List),
            (Rest, List), (SeeAlso, Resource), (IsDefinedBy, Resource), (Comment, Resource), (Label, Resource),
            (Value, Resource),
        ];
        (RdfTerm, RdfTerm)[] ranges =
        [
            (Type, Class), (Domain, Class), (Range, Class), (SubPropertyOf, Property), (SubClassOf, Class),
            (Subject, Resource), (Predicate, Resource), (Object, Resource), (Member, Resource), (First, Resource),
            (Rest, List), (SeeAlso, Resource), (IsDefinedBy, Resource), (Comment, Literal), (Label, Literal),
            (Value, Resource),
        ];

        foreach ((RdfTerm property, RdfTerm @class) in domains)
        {
            yield return (property, Domain, @class);
        }

        foreach ((RdfTerm property, RdfTerm @class) in ranges)
        {
            yield return (property, Range, @class);
        }

        yield return (Alt, SubClassOf, Container);
        yield return (Bag, SubClassOf, Container);
        yield return (Seq, SubClassOf, Container);
        yield return (ContainerMembershipProperty, SubClassOf, Property);
        yield return (IsDefinedBy, SubPropertyOf, SeeAlso);
        yield return (Datatype, SubClassOf, Class);
    }

    /// <summary>The axiomatic triples of one <c>rdf:_n</c>, RDF's and, under RDFS, RDFS's.</summary>
    internal static IEnumerable<(RdfTerm S, RdfTerm P, RdfTerm O)> ContainerMembershipAxioms(RdfTerm term, bool rdfs)
    {
        yield return (term, Type, Property);
        if (rdfs)
        {
            yield return (term, Type, ContainerMembershipProperty);
            yield return (term, Domain, Resource);
            yield return (term, Range, Resource);
        }
    }

    /// <summary>Whether <paramref name="term"/> is <c>rdf:_n</c> for a positive integer n without a leading zero.</summary>
    internal static bool IsContainerMembership(RdfTerm term)
    {
        if (term.Kind != RdfTermKind.Iri)
        {
            return false;
        }

        ReadOnlySpan<byte> lexical = term.Lexical;
        ReadOnlySpan<byte> prefix = "http://www.w3.org/1999/02/22-rdf-syntax-ns#_"u8;
        if (!lexical.StartsWith(prefix) || lexical.Length == prefix.Length || lexical[prefix.Length] == (byte)'0')
        {
            return false;
        }

        foreach (byte b in lexical[prefix.Length..])
        {
            if (b is < (byte)'0' or > (byte)'9')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The regular expression a query uses for <see cref="IsContainerMembership"/>.</summary>
    internal const string ContainerMembershipPattern = "^http://www\\.w3\\.org/1999/02/22-rdf-syntax-ns#_[1-9][0-9]*$";

    private static RdfTerm Rdf(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes(RdfNamespace + local));

    private static RdfTerm Rdfs(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes(RdfsNamespace + local));

    private static RdfTerm Xsd(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes(XsdNamespace + local));
}
