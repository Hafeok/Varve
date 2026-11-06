// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Immutable;
using System.Text;
using Varve.Rdf;

namespace Varve.Inference;

/// <summary>
/// The terms the rules name (RDF 1.1 Semantics §8–9, OWL 2 Profiles §4.3),
/// as terms: a program turns each into a handle of the source it runs over
/// (<see cref="IInferenceTerms.Handle"/>) once, when it is compiled.
/// </summary>
internal static class Vocabulary
{
    internal const string RdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    internal const string RdfsNamespace = "http://www.w3.org/2000/01/rdf-schema#";
    internal const string OwlNamespace = "http://www.w3.org/2002/07/owl#";
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

    /// <summary>The datatypes the RDFS profile recognises (RDF 1.1 Semantics §9.2).</summary>
    internal static readonly ImmutableArray<RdfTerm> RdfsDatatypes = [XsdString, LangString];

    /// <summary>
    /// The RDF axiomatic triples (RDF 1.1 Semantics §8.1), less those of
    /// <c>rdf:_n</c>, which hold only for the ones that occur.
    /// </summary>
    internal static (RdfTerm S, RdfTerm P, RdfTerm O)[] RdfAxioms() =>
    [
        (Type, Type, Property), (Subject, Type, Property), (Predicate, Type, Property), (Object, Type, Property),
        (First, Type, Property), (Rest, Type, Property), (Value, Type, Property), (Nil, Type, List),
    ];

    /// <summary>The RDFS axiomatic triples (RDF 1.1 Semantics §9.1), less those of <c>rdf:_n</c>.</summary>
    internal static (RdfTerm S, RdfTerm P, RdfTerm O)[] RdfsAxioms() =>
    [
        (Type, Domain, Resource), (Domain, Domain, Property), (Range, Domain, Property),
        (SubPropertyOf, Domain, Property), (SubClassOf, Domain, Class), (Subject, Domain, Statement),
        (Predicate, Domain, Statement), (Object, Domain, Statement), (Member, Domain, Resource),
        (First, Domain, List), (Rest, Domain, List), (SeeAlso, Domain, Resource),
        (IsDefinedBy, Domain, Resource), (Comment, Domain, Resource), (Label, Domain, Resource),
        (Value, Domain, Resource),
        (Type, Range, Class), (Domain, Range, Class), (Range, Range, Class), (SubPropertyOf, Range, Property),
        (SubClassOf, Range, Class), (Subject, Range, Resource), (Predicate, Range, Resource),
        (Object, Range, Resource), (Member, Range, Resource), (First, Range, Resource), (Rest, Range, List),
        (SeeAlso, Range, Resource), (IsDefinedBy, Range, Resource), (Comment, Range, Literal),
        (Label, Range, Literal), (Value, Range, Resource),
        (Alt, SubClassOf, Container), (Bag, SubClassOf, Container), (Seq, SubClassOf, Container),
        (ContainerMembershipProperty, SubClassOf, Property),
        (IsDefinedBy, SubPropertyOf, SeeAlso),
        (Datatype, SubClassOf, Class),
    ];

    internal static RdfTerm Rdf(string local) => Iri(RdfNamespace + local);

    internal static RdfTerm Rdfs(string local) => Iri(RdfsNamespace + local);

    internal static RdfTerm Owl(string local) => Iri(OwlNamespace + local);

    internal static RdfTerm Xsd(string local) => Iri(XsdNamespace + local);

    internal static RdfTerm Iri(string iri) => RdfTerm.Iri(Encoding.UTF8.GetBytes(iri));
}
