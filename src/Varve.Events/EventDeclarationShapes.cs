// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.IO;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Events;

/// <summary>
/// SHACL shapes for the declaration vocabulary (ADR 0157):
/// <c>varve:EventTypeShape</c> and <c>varve:EventFieldShape</c>, SHACL Core
/// only, for one catalogue graph as the data graph. What Core cannot state,
/// <see cref="EventDeclarations"/> checks alone; the two agree, by property,
/// on everything the shapes state. This package does not validate by them: a
/// caller who does composes them with a SHACL validator.
/// </summary>
[Contract(typeof(ShapesForEventDeclarations.EventDeclarationShapesInEvents), Role = "the shapes of the declaration vocabulary")]
public static class EventDeclarationShapes
{
    private const string ResourceName = "Varve.Events.EventDeclarationShapes.ttl";

    /// <summary>The shapes graph, as Turtle.</summary>
    [DesignDecision(typeof(ShapesForEventDeclarations.EventDeclarationShapesInEvents), Scope = ExceptionScope.Boundary)]
    public static string Turtle { get; } = Read();

    private static string Read()
    {
        using Stream stream = typeof(EventDeclarationShapes).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("The assembly does not carry " + ResourceName + ".");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
