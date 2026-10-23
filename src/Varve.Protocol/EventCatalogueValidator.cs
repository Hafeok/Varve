// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Delta.Model;
using Varve.Events;
using Varve.Events.Model;
using Varve.Json.Canonical;
using Varve.Protocol.Endpoints;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Protocol;

/// <summary>
/// The event catalogue's check, bound to a dataset (ADRs 0058, 0145): every
/// commit whose effective delta touches a graph under the catalogue namespace
/// is checked by <see cref="EventDeclarationCheck"/>, on every write path, and
/// a refusal names its own problem type. A commit that touches no catalogue
/// graph costs one decision per distinct graph in its delta.
/// </summary>
[Contract(typeof(TheEventSchemaRegistry.CompatibilityCheckedAtCommit), Role = "the dataset validator that checks event declarations")]
public sealed class EventCatalogueValidator : ICommitValidator
{
    /// <inheritdoc />
    public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        Dictionary<TermHandle, RdfTerm?> catalogues = new(proposed.TermComparer);
        List<FeedChange> changes = [];
        Collect(proposed, delta.Asserted, FeedChangeKind.Assert, catalogues, changes);
        Collect(proposed, delta.Retracted, FeedChangeKind.Retract, catalogues, changes);

        if (changes.Count == 0)
        {
            return ValidationVerdict.Accept();
        }

        DeclarationVerdict verdict = EventDeclarationCheck.Check(proposed, changes);
        return verdict.Accepted ? ValidationVerdict.Accept() : Refuse(verdict);
    }

    /// <summary>The refusal of a check's verdict, as its own problem type (ADR 0145 point 6).</summary>
    internal static ValidationVerdict Refuse(DeclarationVerdict verdict)
    {
        ArrayBufferWriter<byte> json = new();
        CanonicalJsonWriter writer = new(json);
        writer.StartObject();
        writer.WriteName("declaration"u8);
        writer.WriteString(verdict.Declaration!.Lexical);

        if (verdict.Detail is not null)
        {
            writer.WriteName("detail"u8);
            writer.WriteString(verdict.Detail);
        }

        ProblemType type;

        if (verdict.ProblemType == DeclarationVerdict.IncompatibleType)
        {
            type = ProblemType.EventSchemaIncompatible;
            writer.WriteName("fields"u8);
            writer.StartArray();

            foreach (FieldChange field in verdict.Fields)
            {
                writer.StartObject();
                writer.WriteName("change"u8);
                writer.WriteString(field.Change);
                writer.WriteName("field"u8);
                writer.WriteString(field.Field);
                writer.EndObject();
            }

            writer.EndArray();
        }
        else
        {
            type = ProblemType.EventDeclarationInvalid;
            writer.WriteName("reason"u8);
            writer.WriteString(verdict.Reason!);
        }

        writer.EndObject();
        return ValidationVerdict.Reject(ValidatorProblem.Report(type, json.WrittenSpan.ToArray()));
    }

    /// <summary>The refusal of one problem with a declaration, as <c>event-declaration-invalid</c>.</summary>
    internal static ValidationVerdict Refuse(DeclarationProblem problem)
    {
        ArrayBufferWriter<byte> json = new();
        CanonicalJsonWriter writer = new(json);
        writer.StartObject();
        writer.WriteName("declaration"u8);
        writer.WriteString(problem.Declaration.Lexical);
        writer.WriteName("detail"u8);
        writer.WriteString(problem.Detail);
        writer.WriteName("reason"u8);
        writer.WriteString(problem.Reason);
        writer.EndObject();
        return ValidationVerdict.Reject(ValidatorProblem.Report(ProblemType.EventDeclarationInvalid, json.WrittenSpan.ToArray()));
    }

    private static void Collect(
        IQuadSource proposed, ReadOnlySpan<Quad> quads, FeedChangeKind kind, Dictionary<TermHandle, RdfTerm?> catalogues, List<FeedChange> changes)
    {
        foreach (Quad quad in quads)
        {
            if (quad.IsDefaultGraph)
            {
                continue;
            }

            if (!catalogues.TryGetValue(quad.Graph, out RdfTerm? graph))
            {
                graph = proposed.TryExternalise(quad.Graph, out RdfTerm? term) && EventCatalogue.IsUnderNamespace(term) ? term : null;
                catalogues[quad.Graph] = graph;
            }

            if (graph is not null
                && proposed.TryExternalise(quad.Subject, out RdfTerm? s)
                && proposed.TryExternalise(quad.Predicate, out RdfTerm? p)
                && proposed.TryExternalise(quad.Object, out RdfTerm? o))
            {
                changes.Add(new FeedChange(kind, s, p, o, graph));
            }
        }
    }
}
