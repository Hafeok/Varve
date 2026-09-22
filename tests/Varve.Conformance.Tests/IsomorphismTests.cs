using System.Collections.Generic;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// The blank node bijection, on cases chosen to defeat each shortcut in it.
/// </summary>
public class IsomorphismTests
{
    private static ParsedQuad Q(string subject, string predicate, string obj, string? graph = null) =>
        new(subject, predicate, obj, graph);

    private const string P = "<http://a/p>";
    private const string Q2 = "<http://a/q>";
    private const string S = "<http://a/s>";
    private const string O = "<http://a/o>";

    private static void Same(IReadOnlyList<ParsedQuad> left, IReadOnlyList<ParsedQuad> right) =>
        Assert.Null(Isomorphism.Compare(left, right));

    private static void Different(IReadOnlyList<ParsedQuad> left, IReadOnlyList<ParsedQuad> right) =>
        Assert.NotNull(Isomorphism.Compare(left, right));

    [Fact]
    public void two_empty_datasets_are_the_same() => Same([], []);

    [Fact]
    public void ground_quads_compare_by_value() => Same([Q(S, P, O)], [Q(S, P, O)]);

    [Fact]
    public void a_missing_ground_quad_is_reported() => Different([Q(S, P, O)], [Q(S, P, S)]);

    [Fact]
    public void a_different_count_is_reported() =>
        Assert.Contains("expected 2", Isomorphism.Compare([Q(S, P, O)], [Q(S, P, O), Q(S, Q2, O)])!,
            System.StringComparison.Ordinal);

    [Fact]
    public void order_does_not_matter() =>
        Same([Q(S, P, O), Q(S, Q2, O)], [Q(S, Q2, O), Q(S, P, O)]);

    [Fact]
    public void one_blank_node_maps_to_one_blank_node() =>
        Same([Q("_:a", P, O)], [Q("_:zzz", P, O)]);

    [Fact]
    public void a_blank_node_does_not_map_to_an_iri() =>
        Different([Q("_:a", P, O)], [Q(S, P, O)]);

    [Fact]
    public void the_same_blank_node_twice_must_stay_the_same_node() =>
        Same([Q("_:a", P, O), Q("_:a", Q2, O)], [Q("_:x", P, O), Q("_:x", Q2, O)]);

    [Fact]
    public void two_nodes_may_not_collapse_into_one()
    {
        // Left has two distinct nodes, right has one used twice.
        Different([Q("_:a", P, O), Q("_:b", Q2, O)], [Q("_:x", P, O), Q("_:x", Q2, O)]);
    }

    [Fact]
    public void a_cycle_maps_onto_a_cycle() =>
        Same([Q("_:a", P, "_:b"), Q("_:b", P, "_:a")], [Q("_:x", P, "_:y"), Q("_:y", P, "_:x")]);

    [Fact]
    public void a_cycle_is_not_two_self_loops()
    {
        // Signature pruning cannot separate these: in both datasets each node
        // appears once as a subject and once as an object of "_:* p _:*". Only
        // the search can tell them apart, so this is the case that proves the
        // search is doing the work rather than the shortcut.
        Different(
            [Q("_:a", P, "_:b"), Q("_:b", P, "_:a")],
            [Q("_:x", P, "_:x"), Q("_:y", P, "_:y")]);
    }

    [Fact]
    public void a_chain_maps_onto_a_chain_only_the_right_way_round()
    {
        Same(
            [Q("_:a", P, "_:b"), Q("_:b", Q2, O)],
            [Q("_:x", P, "_:y"), Q("_:y", Q2, O)]);

        Different(
            [Q("_:a", P, "_:b"), Q("_:b", Q2, O)],
            [Q("_:x", P, "_:y"), Q("_:x", Q2, O)]);
    }

    [Fact]
    public void a_graph_position_is_part_of_the_mapping()
    {
        Same([Q(S, P, O, "_:g")], [Q(S, P, O, "_:h")]);
        Different([Q(S, P, O, "_:g")], [Q(S, P, O)]);
    }

    [Fact]
    public void a_blank_node_used_as_a_graph_and_as_a_subject_is_one_node()
    {
        Same(
            [Q("_:g", P, O), Q(S, P, O, "_:g")],
            [Q("_:h", P, O), Q(S, P, O, "_:h")]);

        Different(
            [Q("_:g", P, O), Q(S, P, O, "_:g")],
            [Q("_:h", P, O), Q(S, P, O, "_:i")]);
    }

    [Fact]
    public void literals_compare_exactly()
    {
        Different([Q(S, P, "\"1\"")], [Q(S, P, "\"01\"")]);
        Different([Q(S, P, "\"x\"@en")], [Q(S, P, "\"x\"@fr")]);
        Same([Q(S, P, "\"x\"@en")], [Q(S, P, "\"x\"@en")]);
    }

    [Fact]
    public void a_larger_symmetric_graph_still_resolves()
    {
        // Six nodes in a ring, relabelled by a rotation. Signature pruning
        // leaves every node a candidate for every other, so this is the search
        // running at its widest on a shape the suites really contain.
        List<ParsedQuad> ring = [];
        List<ParsedQuad> rotated = [];

        for (int i = 0; i < 6; i++)
        {
            ring.Add(Q($"_:a{i}", P, $"_:a{(i + 1) % 6}"));
            rotated.Add(Q($"_:b{(i + 2) % 6}", P, $"_:b{(i + 3) % 6}"));
        }

        Same(ring, rotated);
    }

    [Fact]
    public void a_ring_is_not_two_triangles()
    {
        List<ParsedQuad> ring = [];
        List<ParsedQuad> triangles = [];

        for (int i = 0; i < 6; i++)
        {
            ring.Add(Q($"_:a{i}", P, $"_:a{(i + 1) % 6}"));
        }

        for (int i = 0; i < 3; i++)
        {
            triangles.Add(Q($"_:b{i}", P, $"_:b{(i + 1) % 3}"));
            triangles.Add(Q($"_:c{i}", P, $"_:c{(i + 1) % 3}"));
        }

        Different(ring, triangles);
    }

    [Fact]
    public void the_reason_names_what_differs()
    {
        string? reason = Isomorphism.Compare([Q("_:a", P, O)], [Q("_:x", P, S)]);

        Assert.NotNull(reason);
        Assert.Contains("no blank node in the expected dataset can be _:a", reason,
            System.StringComparison.Ordinal);
    }
}
