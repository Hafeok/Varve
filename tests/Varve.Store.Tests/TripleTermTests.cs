// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>Triple terms as the store's sources hand them out, by value and by handle (ADR 0121).</summary>
public class TripleTermTests
{
    private static RdfTerm Triple(bool blankInside) =>
        RdfTerm.TripleTerm(blankInside ? T.Blank("b") : T.Iri("ts"), T.Iri("tp"), T.Literal("to"));

    /// <summary>
    /// A validator reads the pending source (spec T1 step 5): a triple term
    /// the commit creates is that triple term there, as it is in the store
    /// once the commit lands. #109 suspected otherwise from
    /// <c>PendingSource</c>'s constructor alone; the resolver fills a final
    /// triple allocation's term before the pending source reads it, and this
    /// keeps it so.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task a_validator_externalises_a_fresh_triple_term_as_the_store_does_after_the_commit(bool blankInside)
    {
        await using Dataset dataset = await T.Open(new MemoryStorage());
        Externalising validator = new();

        CommitResult result = await dataset.CommitAsync(
            new CommitRequest { Validators = [validator] }.Assert(T.Iri("r"), T.Iri("reifies"), Triple(blankInside)), T.Ct);

        Assert.Equal(CommitOutcome.Committed, result.Outcome);
        using DatasetView view = dataset.Pin();
        Quad stored = Assert.Single(T.All(view));
        Assert.True(view.TryExternalise(stored.Object, out RdfTerm? after));
        Assert.Equal(RdfTermKind.TripleTerm, after.Kind);
        Assert.Equal(after, validator.Seen);
    }

    private sealed class Externalising : ICommitValidator
    {
        public RdfTerm? Seen { get; private set; }

        public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta)
        {
            Seen = proposed.TryExternalise(delta.Asserted[0].Object, out RdfTerm? term) ? term : null;
            return ValidationVerdict.Accept();
        }
    }
}
