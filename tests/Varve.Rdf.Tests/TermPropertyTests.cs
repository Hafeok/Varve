// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using CsCheck;
using Xunit;

namespace Varve.Rdf.Tests;

/// <summary>
/// The invariants that hold for every term, rather than for the handful a
/// worked example happens to name.
/// </summary>
public class TermPropertyTests
{
    private static readonly Gen<string> Text =
        Gen.String[Gen.Char.AlphaNumeric, 0, 12];

    private static readonly Gen<string> Language =
        Gen.OneOfConst("en", "EN", "en-GB", "EN-gb", "de", "fr-CA", "");

    private static readonly Gen<TextDirection> Direction =
        Gen.OneOfConst(TextDirection.None, TextDirection.LeftToRight, TextDirection.RightToLeft);

    private static readonly Gen<RdfTerm> Term = Gen.Recursive<RdfTerm>((depth, term) =>
        Gen.Int[0, depth > 2 ? 2 : 3].SelectMany(kind => kind switch
        {
            0 => Text.Select(t => RdfTerm.Iri(Encoding.UTF8.GetBytes("http://a/" + t))),
            1 => Text.Select(t => RdfTerm.BlankNode(Encoding.UTF8.GetBytes("b" + t))),
            2 => Gen.Select(Text, Language, Direction, (lexical, language, direction) =>
                language.Length == 0
                    ? RdfTerm.Literal(Encoding.UTF8.GetBytes(lexical))
                    : RdfTerm.Literal(
                        Encoding.UTF8.GetBytes(lexical),
                        Encoding.UTF8.GetBytes(language),
                        direction)),
            _ => Gen.Select(term, term, term, RdfTerm.TripleTerm),
        }));

    [Fact]
    public void equal_terms_have_equal_hashes()
    {
        Gen.Select(Term, Term).Sample((left, right) =>
            !left.Equals(right) || left.GetHashCode() == right.GetHashCode());
    }

    [Fact]
    public void equality_is_reflexive_and_symmetric()
    {
        Gen.Select(Term, Term).Sample((left, right) =>
            left.Equals(left) && right.Equals(right) && left.Equals(right) == right.Equals(left));
    }

    [Fact]
    public void interning_a_term_twice_gives_one_handle()
    {
        Term.Sample(term =>
        {
            InMemoryDataset dataset = new();
            return dataset.Internalise(term) == dataset.Internalise(term) && dataset.TermCount == 1;
        });
    }

    [Fact]
    public void every_interned_term_comes_back_out_equal()
    {
        Term.List[1, 20].Sample(terms =>
        {
            InMemoryDataset dataset = new();
            List<TermHandle> handles = [];

            foreach (RdfTerm term in terms)
            {
                handles.Add(dataset.Internalise(term));
            }

            for (int i = 0; i < terms.Count; i++)
            {
                if (!dataset.TryExternalise(handles[i], out RdfTerm? back) || !back.Equals(terms[i]))
                {
                    return false;
                }
            }

            return true;
        });
    }

    [Fact]
    public void a_terms_reported_datatype_never_depends_on_how_it_was_written()
    {
        Text.Sample(lexical =>
        {
            byte[] bytes = Encoding.UTF8.GetBytes(lexical);
            RdfTerm shorthand = RdfTerm.Literal(bytes);
            RdfTerm spelt = RdfTerm.Literal(bytes, RdfTerm.Iri(RdfVocabulary.XsdString));

            return shorthand.Equals(spelt)
                && shorthand.DatatypeIri.SequenceEqual(spelt.DatatypeIri);
        });
    }
}
