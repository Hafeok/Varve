// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Events;
using Varve.Json.Canonical;
using Varve.Protocol.Endpoints;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Protocol;

/// <summary>
/// The freeze of a dataset's catalogue graphs (ADR 0167): a commit authorised
/// at a management position whose declarations say the dataset's event
/// declarations are <c>varve:Moving</c> or <c>varve:Moved</c> carries this
/// validator, which refuses it when it changes a graph under the catalogue
/// namespace, as <c>409 event-catalogue-moved</c>. A commit that touches no
/// catalogue graph costs one decision per distinct graph in its delta.
/// </summary>
[Contract(typeof(EventDeclarationsMoveToTheManagementPlane.CatalogueWritesRefusedAfterTheMove), Role = "the validator that refuses catalogue writes once the declarations moved")]
public sealed class EventCatalogueFreeze : ICommitValidator
{
    private readonly DatasetName _dataset;

    /// <summary>The freeze of <paramref name="dataset"/>'s catalogue graphs.</summary>
    public EventCatalogueFreeze(DatasetName dataset) => _dataset = dataset;

    /// <inheritdoc />
    public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta)
    {
        ArgumentNullException.ThrowIfNull(proposed);

        if (!Touches(proposed, delta.Asserted) && !Touches(proposed, delta.Retracted))
        {
            return ValidationVerdict.Accept();
        }

        // The path of the dataset's declarations in their new home.
        string manage = "/manage/datasets/" + _dataset.Value + "/events";
        ArrayBufferWriter<byte> json = new();
        CanonicalJsonWriter writer = new(json);
        writer.StartObject();
        writer.WriteName("detail"u8);
        writer.WriteString("The event declarations of dataset " + _dataset.Value + " are on the management plane (ADR 0167): its catalogue graphs take no write; declare event types at " + manage + ".");
        writer.WriteName("manage"u8);
        writer.WriteString(manage);
        writer.EndObject();
        return ValidationVerdict.Reject(ValidatorProblem.Report(ProblemType.EventCatalogueMoved, json.WrittenSpan.ToArray()));
    }

    private static bool Touches(IQuadSource proposed, ReadOnlySpan<Quad> quads)
    {
        TermHandle last = TermHandle.None;

        foreach (Quad quad in quads)
        {
            if (quad.IsDefaultGraph || quad.Graph.Equals(last))
            {
                continue;
            }

            last = quad.Graph;

            if (proposed.TryExternalise(quad.Graph, out RdfTerm? graph) && EventCatalogue.IsUnderNamespace(graph))
            {
                return true;
            }
        }

        return false;
    }
}
