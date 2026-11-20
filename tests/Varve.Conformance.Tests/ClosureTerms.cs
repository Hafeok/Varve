// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Varve.Inference;
using Varve.Inference.Model;
using Varve.Rdf;
using Varve.Sparql.Entailment;

namespace Varve.Conformance.Tests;

/// <summary>Handles over terms, recognising a case's datatypes, their value spaces Varve.Xsd's, and rdf:XMLLiteral's well-formed XML.</summary>
internal sealed class ClosureTerms(IReadOnlyList<RdfTerm> recognised) : IInferenceTerms
{
    private const string RdfNs = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private readonly Dictionary<RdfTerm, TermHandle> _handles = new(RdfTerm.Comparer);
    private readonly List<RdfTerm> _terms = [];

    public TermHandle Handle(RdfTerm term)
    {
        if (!_handles.TryGetValue(term, out TermHandle handle))
        {
            _terms.Add(term);
            handle = new TermHandle((ulong)_terms.Count);
            _handles[term] = handle;
        }

        return handle;
    }

    public bool IsLiteral(TermHandle handle) => Term(handle).Kind == RdfTermKind.Literal;

    public bool TryDatatype(TermHandle literal, out TermHandle datatype)
    {
        RdfTerm term = Term(literal);
        datatype = term.Kind == RdfTermKind.Literal ? Handle(RdfTerm.Iri(term.DatatypeIri)) : TermHandle.None;
        return term.Kind == RdfTermKind.Literal;
    }

    public bool IsContainerMembership(TermHandle handle)
    {
        RdfTerm term = Term(handle);
        string iri = Encoding.UTF8.GetString(term.Lexical);
        return term.Kind == RdfTermKind.Iri && iri.StartsWith(RdfNs + "_", StringComparison.Ordinal) && iri.Length > RdfNs.Length + 1
            && iri[RdfNs.Length + 1] != '0' && iri.AsSpan(RdfNs.Length + 1).IndexOfAnyExceptInRange('0', '9') < 0;
    }

    public IReadOnlyCollection<RdfTerm> RecognisedDatatypes(InferenceProfile profile) => recognised;

    public bool HasValueType(TermHandle literal, TermHandle datatype)
    {
        RdfTerm value = Term(literal), type = Term(datatype);
        if (value.Kind != RdfTermKind.Literal)
        {
            return false;
        }

        return Encoding.UTF8.GetString(value.DatatypeIri) == RdfNs + "XMLLiteral"
            ? type.Lexical.SequenceEqual(value.DatatypeIri) && WellFormed(Encoding.UTF8.GetString(value.Lexical))
            : LiteralValues.IsWellTyped(value) && LiteralValues.HasValueType(value, type);
    }

    public bool ValueSpaceWithin(TermHandle datatype, TermHandle super) => LiteralValues.ValueSpaceWithin(Term(datatype), Term(super));

    private static bool WellFormed(string xml)
    {
        try
        {
            using XmlReader reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { ConformanceLevel = ConformanceLevel.Fragment, DtdProcessing = DtdProcessing.Prohibit });
            while (reader.Read())
            {
            }

            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    internal RdfTerm Term(TermHandle handle) => _terms[(int)handle.Value - 1];
}
