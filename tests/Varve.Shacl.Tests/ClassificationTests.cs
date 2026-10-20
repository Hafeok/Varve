// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Linq;
using Varve.Rdf;
using Varve.Shacl.Model;
using Xunit;

namespace Varve.Shacl.Tests;

/// <summary>ADR 0171's table, construct by construct, through <see cref="ShapesGraph.Classify"/>.</summary>
public class ClassificationTests
{
    [Theory]
    [InlineData("ex:S sh:targetNode ex:n ; sh:property [ sh:path ex:p ; sh:minCount 1 ; sh:datatype xsd:integer ] .", Maintainability.Incremental)]
    [InlineData("ex:S sh:targetSubjectsOf ex:p ; sh:property [ sh:path ( ex:p [ sh:inversePath ex:q ] ) ; sh:maxCount 1 ] .", Maintainability.Incremental)]
    [InlineData("ex:S sh:targetNode ex:n ; sh:property [ sh:path [ sh:alternativePath ( ex:p ex:q ) ] ; sh:uniqueLang true ] .", Maintainability.Incremental)]
    [InlineData("ex:S sh:targetNode ex:n ; sh:closed true ; sh:property [ sh:path ex:p ; sh:equals ex:q ] .", Maintainability.Incremental)]
    [InlineData("ex:S sh:targetNode ex:n ; sh:property [ sh:path ex:p ; sh:uniqueValuesFor ( ex:id ) ] .", Maintainability.Incremental)]
    [InlineData("ex:S sh:targetNode ex:n ; sh:property [ sh:path [ sh:zeroOrMorePath ex:p ] ; sh:nodeKind sh:IRI ] .", Maintainability.Scoped)]
    [InlineData("ex:S sh:targetClass ex:C .", Maintainability.Scoped)]
    [InlineData("ex:S sh:targetNode ex:n ; sh:property [ sh:path ex:p ; sh:class ex:C ] .", Maintainability.Scoped)]
    [InlineData("ex:S sh:targetNode ex:n ; sh:property [ sh:path ex:p ; sh:memberShape ex:T ] . ex:T sh:datatype xsd:integer .", Maintainability.Scoped)]
    [InlineData("ex:S sh:targetNode ex:n ; sh:property [ sh:path ex:p ; sh:node ex:S ] .", Maintainability.Scoped)]
    [InlineData("ex:S sh:targetNode ex:n ; sh:property [ sh:path ex:p ; sh:values ex:k ; sh:node ex:T ] . ex:T sh:nodeKind sh:IRI .", Maintainability.Full)]
    public void The_classification_follows_the_table(string shapes, Maintainability expected)
    {
        ShapesGraph loaded = ShapesGraph.Load(Support.Dataset(shapes), GraphPattern.DefaultGraph, null, TestContext.Current.CancellationToken).Shapes!;
        ShapeClassification shape = loaded.Classify().Shapes.Single(s => s.Shape.Equals(Support.Ex("S")));
        Assert.Equal(expected, shape.Maintainability);
        Assert.Equal(expected == Maintainability.Incremental, shape.Reasons.Count == 0);
    }

    [Fact]
    public void A_reference_inherits_the_class_and_names_the_shape_it_comes_from()
    {
        ShapesGraph loaded = ShapesGraph.Load(
            Support.Dataset("ex:S sh:targetNode ex:n ; sh:property [ sh:path ex:p ; sh:node ex:T ] . ex:T sh:property [ sh:path [ sh:oneOrMorePath ex:q ] ; sh:minCount 1 ] ."),
            GraphPattern.DefaultGraph, null, TestContext.Current.CancellationToken).Shapes!;
        ShapesClassification classification = loaded.Classify();
        ShapeClassification shape = classification.Shapes.Single(s => s.Shape.Equals(Support.Ex("S")));
        Assert.Equal(Maintainability.Scoped, shape.Maintainability);
        Assert.Contains(shape.Reasons, r => r.Construct.Contains("<http://example.com/ns#T>", System.StringComparison.Ordinal) && r.Section == "SHACL 1.2 Core §4.5–4.6");
        Assert.Equal(Maintainability.Scoped, classification.Worst);
    }
}
