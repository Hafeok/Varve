// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CsCheck;
using Varve.Shacl.Model;
using Xunit;

namespace Varve.Shacl.Tests;

/// <summary>
/// ADR 0150's semantics as a property: over generated data and generated
/// positive recursive shapes, the validator's conformance is the greatest
/// fixpoint a naive iteration computes — start with every node conforming to
/// every shape, and withdraw until nothing changes.
/// </summary>
public class RecursionPropertyTests
{
    /// <summary>The number of generated cases each run checks, as reported in the pull request.</summary>
    internal const int Iterations = 2_000;

    private enum AtomKind
    {
        Class,
        AllValues,
        SomeValue,
        MinCountOne,
        Or,
        Node,
    }

    private sealed record Atom(AtomKind Kind, int Class, int Predicate, int Shape, int Other);

    private sealed record Model(int Nodes, (int S, int P, int O)[] Edges, (int Node, int Class)[] Types, Atom[][] Shapes)
    {
        public override string ToString() => Turtle(this) + "\n# data\n" + Data(this);
    }

    private static readonly Gen<Model> Models =
        from nodes in Gen.Int[1, 5]
        from shapes in Gen.Int[1, 4]
        from edges in Gen.Select(Gen.Int[0, nodes - 1], Gen.Int[0, 1], Gen.Int[0, nodes - 1]).Array[0, 10]
        from types in Gen.Select(Gen.Int[0, nodes - 1], Gen.Int[0, 1]).Array[0, 6]
        from atoms in Gen.Select(Gen.Enum<AtomKind>(), Gen.Int[0, 1], Gen.Int[0, 1], Gen.Int[0, shapes - 1], Gen.Int[0, shapes - 1])
            .Select((kind, c, p, s, o) => new Atom(kind, c, p, s, o)).Array[1, 3].Array[shapes]
        select new Model(nodes, edges, types, atoms);

    [Fact]
    public void Conformance_to_positive_recursive_shapes_is_the_greatest_fixpoint()
    {
        Models.Sample(model =>
        {
            ValidationReport report = Support.Validate(Turtle(model), Data(model));
            bool[,] expected = Oracle(model);
            for (int shape = 0; shape < model.Shapes.Length; shape++)
            {
                List<string> failing = Support.Failing(report, "T" + shape);
                for (int node = 0; node < model.Nodes; node++)
                {
                    Assert.True(
                        expected[node, shape] != failing.Contains("n" + node),
                        $"n{node} against S{shape}: the oracle says {(expected[node, shape] ? "conforms" : "does not conform")}");
                }
            }
        }, iter: Iterations);
    }

    /// <summary>The naive greatest fixpoint: everything conforms until an atom fails under the current assignment.</summary>
    private static bool[,] Oracle(Model model)
    {
        int shapes = model.Shapes.Length;
        bool[,] conforms = new bool[model.Nodes, shapes];
        for (int n = 0; n < model.Nodes; n++)
        {
            for (int s = 0; s < shapes; s++)
            {
                conforms[n, s] = true;
            }
        }

        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int n = 0; n < model.Nodes; n++)
            {
                for (int s = 0; s < shapes; s++)
                {
                    if (conforms[n, s] && !Holds(model, conforms, n, s))
                    {
                        conforms[n, s] = false;
                        changed = true;
                    }
                }
            }
        }

        return conforms;
    }

    private static bool Holds(Model model, bool[,] conforms, int node, int shape)
    {
        foreach (Atom atom in model.Shapes[shape])
        {
            List<int> values = [];
            foreach ((int s, int p, int o) in model.Edges)
            {
                if (s == node && p == atom.Predicate && !values.Contains(o))
                {
                    values.Add(o);
                }
            }

            bool holds = atom.Kind switch
            {
                AtomKind.Class => Array.Exists(model.Types, t => t.Node == node && t.Class == atom.Class),
                AtomKind.AllValues => values.TrueForAll(v => conforms[v, atom.Shape]),
                AtomKind.SomeValue => values.Exists(v => conforms[v, atom.Shape]),
                AtomKind.MinCountOne => values.Count > 0,
                AtomKind.Or => conforms[node, atom.Shape] || conforms[node, atom.Other],
                AtomKind.Node => conforms[node, atom.Shape],
                _ => throw new InvalidOperationException(),
            };
            if (!holds)
            {
                return false;
            }
        }

        return true;
    }

    private static string Turtle(Model model)
    {
        StringBuilder text = new();
        for (int s = 0; s < model.Shapes.Length; s++)
        {
            text.Append("ex:S").Append(s).Append(" a sh:NodeShape");
            foreach (Atom atom in model.Shapes[s])
            {
                text.Append(" ;\n    ").Append(atom.Kind switch
                {
                    AtomKind.Class => $"sh:class ex:C{atom.Class}",
                    AtomKind.AllValues => $"sh:property [ sh:path ex:p{atom.Predicate} ; sh:node ex:S{atom.Shape} ]",
                    AtomKind.SomeValue => $"sh:property [ sh:path ex:p{atom.Predicate} ; sh:someValue ex:S{atom.Shape} ]",
                    AtomKind.MinCountOne => $"sh:property [ sh:path ex:p{atom.Predicate} ; sh:minCount 1 ]",
                    AtomKind.Or => $"sh:or ( ex:S{atom.Shape} ex:S{atom.Other} )",
                    AtomKind.Node => $"sh:node ex:S{atom.Shape}",
                    _ => throw new InvalidOperationException(),
                });
            }

            // A wrapper outside every cycle: a result for T at n exactly when n does not conform to S.
            text.Append(" .\nex:T").Append(s).Append(" a sh:NodeShape ; sh:node ex:S").Append(s).Append(" ; sh:targetNode ");
            for (int n = 0; n < model.Nodes; n++)
            {
                text.Append(n == 0 ? string.Empty : ", ").Append("ex:n").Append(n);
            }

            text.Append(" .\n");
        }

        return text.ToString();
    }

    private static string Data(Model model)
    {
        StringBuilder text = new();
        foreach ((int s, int p, int o) in model.Edges)
        {
            text.Append(CultureInfo.InvariantCulture, $"ex:n{s} ex:p{p} ex:n{o} .\n");
        }

        foreach ((int n, int c) in model.Types)
        {
            text.Append(CultureInfo.InvariantCulture, $"ex:n{n} a ex:C{c} .\n");
        }

        return text.ToString();
    }
}
