// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using Varve.Sparql.Algebra;

namespace Varve.Sparql.Evaluation.Compile;

/// <summary>
/// ADR 0054's normalisation, always applied: a path pattern whose top is
/// <c>link</c>, <c>inv</c>, <c>seq</c> or <c>alt</c> becomes a triple pattern,
/// a swapped pattern, a join through a fresh variable, or a union — §18.4's
/// own definitions of those four — recursively, and never inside a closure.
/// Fresh variables are named <c>.p0</c>, <c>.p1</c>, …: <c>VARNAME</c> cannot
/// begin with a dot, so no author's variable is one.
/// </summary>
internal sealed class PathNormaliser : AlgebraRewriter
{
    private int _fresh;

    protected override QueryPattern RewritePathPattern(PathPattern pattern)
    {
        PatternTerm subject = Rewrite(pattern.Subject);
        PatternTerm @object = Rewrite(pattern.Object);
        return Normalise(subject, pattern.Path, @object, pattern.Span);
    }

    private QueryPattern Normalise(PatternTerm x, PropertyPath path, PatternTerm y, SourceSpan span)
    {
        switch (path)
        {
            case PredicatePath link:
                return new Bgp(AlgebraList.Of(new TriplePattern(x, new TermPattern(link.Predicate) { Span = span }, y) { Span = span })) { Span = span };
            case InversePath inverse:
                return Normalise(y, inverse.Inner, x, span);
            case SequencePath sequence:
                VariablePattern middle = new(new Variable(".p" + (_fresh++).ToString(CultureInfo.InvariantCulture))) { Span = span };
                return new Join(Normalise(x, sequence.Left, middle, span), Normalise(middle, sequence.Right, y, span)) { Span = span };
            case AlternativePath alternative:
                return new Union(Normalise(x, alternative.Left, y, span), Normalise(x, alternative.Right, y, span)) { Span = span };
            default:
                return new PathPattern(x, path, y) { Span = span };
        }
    }
}
