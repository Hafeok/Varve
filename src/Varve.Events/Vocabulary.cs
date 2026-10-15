// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Rdf;

namespace Varve.Events;

/// <summary>The terms of the event catalogue's vocabulary (docs/spec/events.md §3.2), and the RDF and XSD terms it uses.</summary>
internal static class Vocabulary
{
    internal const string Varve = "https://w3id.org/varve/ns#";
    internal const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    internal const string Rdfs = "http://www.w3.org/2000/01/rdf-schema#";
    internal const string Xsd = "http://www.w3.org/2001/XMLSchema#";

    internal const string EventTypeIri = Varve + "EventType";
    internal const string EventFieldIri = Varve + "EventField";
    internal const string EventNameIri = Varve + "eventName";
    internal const string EventClassIri = Varve + "eventClass";
    internal const string EventVersionIri = Varve + "eventVersion";
    internal const string EventSelectIri = Varve + "eventSelect";
    internal const string EventSubjectIri = Varve + "eventSubject";
    internal const string EventRetiredIri = Varve + "eventRetired";
    internal const string EventFieldPropertyIri = Varve + "eventField";
    internal const string FieldNameIri = Varve + "fieldName";
    internal const string FieldDatatypeIri = Varve + "fieldDatatype";
    internal const string FieldRequiredIri = Varve + "fieldRequired";
    internal const string FieldAsIri = Varve + "fieldAs";
    internal const string FieldKeepLanguageIri = Varve + "fieldKeepLanguage";

    internal const string TypeIri = Rdf + "type";
    internal const string LangStringIri = Rdf + "langString";
    internal const string DirLangStringIri = Rdf + "dirLangString";
    internal const string CommentIri = Rdfs + "comment";
    internal const string StringIri = Xsd + "string";
    internal const string BooleanIri = Xsd + "boolean";
    internal const string IntegerIri = Xsd + "integer";
    internal const string AnyUriIri = Xsd + "anyURI";

    internal static readonly RdfTerm Type = Iri(TypeIri);
    internal static readonly RdfTerm EventType = Iri(EventTypeIri);
    internal static readonly RdfTerm EventField = Iri(EventFieldIri);

    internal static RdfTerm Iri(string iri) => RdfTerm.Iri(System.Text.Encoding.UTF8.GetBytes(iri));
}
