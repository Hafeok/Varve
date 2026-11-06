// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using Varve.Inference.Model;
using Varve.Rdf;

namespace Varve.Inference.Tests;

/// <summary>A dictionary of terms to handles, counted from 1, as a store's is: the tests' source of handles.</summary>
internal sealed class Terms : IInferenceTerms
{
    internal const string RdfNs = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    internal const string RdfsNs = "http://www.w3.org/2000/01/rdf-schema#";
    internal const string XsdNs = "http://www.w3.org/2001/XMLSchema#";

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
        if (term.Kind != RdfTermKind.Literal)
        {
            datatype = TermHandle.None;
            return false;
        }

        datatype = Handle(RdfTerm.Iri(term.DatatypeIri));
        return true;
    }

    public bool IsContainerMembership(TermHandle handle)
    {
        RdfTerm term = Term(handle);
        if (term.Kind != RdfTermKind.Iri)
        {
            return false;
        }

        string iri = Encoding.UTF8.GetString(term.Lexical);
        if (!iri.StartsWith(RdfNs + "_", System.StringComparison.Ordinal) || iri.Length == RdfNs.Length + 1)
        {
            return false;
        }

        string digits = iri[(RdfNs.Length + 1)..];
        return digits[0] != '0' && digits.AsSpan().IndexOfAnyExceptInRange('0', '9') < 0;
    }

    internal RdfTerm Term(TermHandle handle) => _terms[(int)handle.Value - 1];

    internal Fact Fact(RdfTerm s, RdfTerm p, RdfTerm o) => new(Handle(s), Handle(p), Handle(o));

    internal string Text(Fact fact) => $"{Text(fact.Subject)} {Text(fact.Predicate)} {Text(fact.Object)}";

    private string Text(TermHandle handle)
    {
        RdfTerm term = Term(handle);
        string lexical = Encoding.UTF8.GetString(term.Lexical);
        return term.Kind switch
        {
            RdfTermKind.Iri => "<" + lexical + ">",
            RdfTermKind.BlankNode => "_:" + lexical,
            _ => "\"" + lexical + "\"^^<" + Encoding.UTF8.GetString(term.DatatypeIri) + ">",
        };
    }

    internal static RdfTerm IriOf(string iri) => RdfTerm.Iri(Encoding.UTF8.GetBytes(iri));

    internal static RdfTerm Named(string local) => IriOf("http://example.org/" + local);
}
