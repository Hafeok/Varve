// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;
using Xunit;

namespace Varve.Store.Tests;

public class SmokeTests
{
    [Fact]
    public async Task a_commit_is_visible_to_the_next_pin_and_survives_reopening()
    {
        MemoryStorage storage = new();
        await using (Dataset dataset = await T.Open(storage))
        {
            CommitResult result = await dataset.CommitAsync(new CommitRequest()
                .Assert(T.Iri("s"), T.Iri("p"), T.Literal("o"))
                .Assert(T.Iri("s"), T.Iri("p"), T.Integer("42"), T.Iri("g")), T.Ct);

            Assert.Equal(CommitOutcome.Committed, result.Outcome);
            Assert.Equal(1, result.Position);

            using DatasetView view = dataset.Pin();
            Assert.Equal(2, T.All(view).Count);
            Assert.Contains("<http://example.org/s> <http://example.org/p> \"o\"^^<http://www.w3.org/2001/XMLSchema#string>", T.Terms(view));
        }

        await using Dataset reopened = await T.Open(storage);
        Assert.Equal(1, reopened.Head);
        using DatasetView again = reopened.Pin();
        Assert.Equal(2, T.All(again).Count);
    }
}
