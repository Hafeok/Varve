// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Xunit;

namespace Varve.Rdf.Tests;

public class HandleTests
{
    [Fact]
    public void the_none_handle_is_zero()
    {
        Assert.True(TermHandle.None.IsNone);
        Assert.Equal(0UL, TermHandle.None.Value);
        Assert.False(new TermHandle(1).IsNone);
    }

    [Fact]
    public void handles_compare_by_value()
    {
        Assert.True(new TermHandle(7) == new TermHandle(7));
        Assert.True(new TermHandle(7) != new TermHandle(8));
        Assert.Equal(new TermHandle(7).GetHashCode(), new TermHandle(7).GetHashCode());
    }

    [Fact]
    public void a_quad_without_a_graph_is_in_the_default_graph()
    {
        Quad quad = new(new TermHandle(1), new TermHandle(2), new TermHandle(3));

        Assert.True(quad.IsDefaultGraph);
        Assert.True(quad.Graph.IsNone);
    }

    [Fact]
    public void quads_compare_by_all_four_positions()
    {
        Quad left = new(new TermHandle(1), new TermHandle(2), new TermHandle(3), new TermHandle(4));

        Assert.Equal(left, new Quad(new TermHandle(1), new TermHandle(2), new TermHandle(3), new TermHandle(4)));
        Assert.NotEqual(left, new Quad(new TermHandle(1), new TermHandle(2), new TermHandle(3)));
        Assert.NotEqual(left, new Quad(new TermHandle(9), new TermHandle(2), new TermHandle(3), new TermHandle(4)));
    }
}
