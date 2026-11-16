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
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// <c>rdf11/rdf-mt</c>'s inconsistency cases on the closure (ADR 0197 point
/// 2): each case whose result is <c>false</c> asks whether its action is
/// inconsistent under its regime with the datatypes it recognises. The
/// action is closed under the RDFS profile, recognising the case's
/// datatypes; a positive case finds a clash, a negative one none. One
/// ratchet line per case, <c>&lt;test IRI&gt;@closure</c>.
/// </summary>
public class RdfMtClosureTests
{
    public static IEnumerable<TheoryDataRow<string>> Cases()
    {
        if (!TestData.IsCheckedOut)
        {
            yield break;
        }

        foreach (RdfMtEntry entry in RdfMtSuite.Entries)
        {
            if (entry.Result is null)
            {
                string id = entry.TestIri + "@closure";
                yield return new TheoryDataRow<string>(id) { TestDisplayName = id };
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Case(string testIri)
    {
        RdfMtEntry entry = RdfMtSuite.ByIri[testIri[..testIri.LastIndexOf('@')]];
        Terms terms = new(entry.Recognised);
        Closure closure = new(InferenceProfile.Rdfs, terms);
        List<Fact> facts = [];
        foreach (DataQuad quad in EvaluationData.Quads(entry.Action))
        {
            facts.Add(new Fact(terms.Handle(quad.Subject), terms.Handle(quad.Predicate), terms.Handle(quad.Object)));
        }

        closure.Apply([.. facts.Distinct()], []);
        IReadOnlyList<Clash> clashes = closure.Clashes();
        Assert.True(
            (clashes.Count > 0) == entry.Positive,
            entry.Name + ": the action is " + (clashes.Count > 0 ? "inconsistent (" + string.Join(", ", clashes.Select(c => Encoding.UTF8.GetString(c.Rule.Lexical))) + ")" : "consistent") + " under " + entry.Regime);
    }

    /// <summary>Handles over terms, recognising a case's datatypes, their value spaces Varve.Xsd's, and rdf:XMLLiteral's well-formed XML.</summary>
    private sealed class Terms(IReadOnlyList<RdfTerm> recognised) : IInferenceTerms
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

        private RdfTerm Term(TermHandle handle) => _terms[(int)handle.Value - 1];
    }
}
