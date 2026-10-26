// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers;
using System.Collections.Generic;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Json.Canonical;
using Varve.Rdf;
using Varve.Shacl.Model;
using Varve.Shacl.Store.Model;
using Varve.Turtle;

namespace Varve.Shacl.Store;

/// <summary>
/// A refusal as a validator's report (ADR 0169, ADR 0145's form): the
/// problem type's IRI and its members as an <c>rdf:JSON</c> literal in
/// canonical form, each term in canonical N-Triples, each finding by focus
/// node and path.
/// </summary>
[Contract(typeof(CommitTimeGatingUnderAShapesBinding.ShapesBindingProblemTypes), Role = "a binding's refusal as a validator's report")]
internal static class Report
{
    private const string Problems = "https://w3id.org/varve/problems/";

    private static readonly RdfTerm RdfJson = RdfTerm.Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#JSON"u8);

    /// <summary><c>shapes-violated</c>: a commit that introduces a violation under a <c>reject</c> binding.</summary>
    internal static IReadOnlyList<RdfTerm> ShapesViolated(ShapesBinding binding, List<ShaclResult> violations, ValidationReport affected, ShapesChange change)
    {
        ArrayBufferWriter<byte> json = new();
        CanonicalJsonWriter writer = new(json);
        writer.StartObject();
        writer.WriteName("binding"u8);
        writer.WriteString(Term(binding.Binding));
        writer.WriteName("detail"u8);
        writer.WriteString(
            violations.Count + (violations.Count == 1 ? " violation" : " violations") + " the pinned position does not have, among the focus nodes this commit affects (ADR 0169)"
            + (change == ShapesChange.Changed ? "; the commit also changes the shapes, so its data was checked against the new shapes, and what the change makes non-conforming elsewhere is the validation projection's to report" : string.Empty)
            + ".");
        writer.WriteName("findings"u8);
        writer.StartArray();
        foreach (ShaclResult result in violations)
        {
            Finding(writer, result);
        }

        writer.EndArray();
        writer.WriteName("report"u8);
        writer.WriteString(Encoding.UTF8.GetString(affected.ToCanonicalNQuads().Span));
        writer.WriteName("shapesChanged"u8);
        writer.WriteBoolean(change == ShapesChange.Changed);
        writer.WriteName("shapesGraph"u8);
        writer.WriteString(Term(binding.ShapesGraph));
        writer.EndObject();
        return Of("shapes-violated", json);
    }

    /// <summary><c>binding-not-incremental</c>: a <c>reject</c> binding with a full shape and no <c>varve:allowFullEvaluation</c>.</summary>
    internal static IReadOnlyList<RdfTerm> BindingNotIncremental(ShapesBinding binding, ShapesClassification classification)
    {
        ArrayBufferWriter<byte> json = new();
        CanonicalJsonWriter writer = new(json);
        writer.StartObject();
        writer.WriteName("binding"u8);
        writer.WriteString(Term(binding.Binding));
        writer.WriteName("detail"u8);
        writer.WriteString("A reject binding validates each commit's affected focus nodes, and these shapes cannot bound theirs: every commit would validate their every target (ADR 0171). Restructure them, or declare varve:allowFullEvaluation true and accept that cost per commit.");
        writer.WriteName("shapes"u8);
        writer.StartArray();
        foreach (ShapeClassification shape in classification.Shapes)
        {
            if (shape.Maintainability != Maintainability.Full)
            {
                continue;
            }

            foreach (ClassificationReason reason in shape.Reasons)
            {
                writer.StartObject();
                writer.WriteName("construct"u8);
                writer.WriteString(reason.Construct);
                writer.WriteName("section"u8);
                writer.WriteString(reason.Section);
                writer.WriteName("shape"u8);
                writer.WriteString(Term(shape.Shape));
                writer.EndObject();
            }
        }

        writer.EndArray();
        writer.EndObject();
        return Of("binding-not-incremental", json);
    }

    /// <summary><c>shapes-invalid</c>: the bound shapes graph is refused, or a validation under it failed.</summary>
    internal static IReadOnlyList<RdfTerm> ShapesInvalid(ShapesBinding binding, IReadOnlyList<ShaclProblem> problems)
    {
        ArrayBufferWriter<byte> json = new();
        CanonicalJsonWriter writer = new(json);
        writer.StartObject();
        writer.WriteName("binding"u8);
        writer.WriteString(Term(binding.Binding));
        writer.WriteName("detail"u8);
        writer.WriteString("The binding's shapes graph " + Term(binding.ShapesGraph) + " is refused, or a validation under it failed (ADR 0151); no commit is decided against it.");
        writer.WriteName("problems"u8);
        writer.StartArray();
        foreach (ShaclProblem problem in problems)
        {
            writer.StartObject();
            if (problem.Cycle is { Count: > 0 } cycle)
            {
                writer.WriteName("cycle"u8);
                writer.StartArray();
                foreach (RdfTerm step in cycle)
                {
                    writer.WriteString(Term(step));
                }

                writer.EndArray();
            }

            if (problem.FocusNode is { } focus)
            {
                writer.WriteName("focusNode"u8);
                writer.WriteString(Term(focus));
            }

            writer.WriteName("kind"u8);
            writer.WriteString(problem.Kind.ToString());
            writer.WriteName("message"u8);
            writer.WriteString(problem.Message);
            if (problem.Shape is { } shape)
            {
                writer.WriteName("shape"u8);
                writer.WriteString(Term(shape));
            }

            writer.EndObject();
        }

        writer.EndArray();
        writer.EndObject();
        return Of("shapes-invalid", json);
    }

    /// <summary>One finding: a result by focus node and path, members in canonical order.</summary>
    private static void Finding(CanonicalJsonWriter writer, ShaclResult result)
    {
        writer.StartObject();
        writer.WriteName("component"u8);
        writer.WriteString(Term(result.SourceConstraintComponent));
        writer.WriteName("focusNode"u8);
        writer.WriteString(Term(result.FocusNode));
        writer.WriteName("messages"u8);
        writer.StartArray();
        foreach (RdfTerm message in result.Messages)
        {
            writer.WriteString(Term(message));
        }

        writer.EndArray();
        if (result.Path is { } path)
        {
            writer.WriteName("path"u8);
            writer.WriteString(Term(path));
        }

        writer.WriteName("severity"u8);
        writer.WriteString(Term(result.Severity));
        writer.WriteName("sourceShape"u8);
        writer.WriteString(Term(result.SourceShape));
        if (result.Value is { } value)
        {
            writer.WriteName("value"u8);
            writer.WriteString(Term(value));
        }

        writer.EndObject();
    }

    private static IReadOnlyList<RdfTerm> Of(string type, ArrayBufferWriter<byte> json) =>
        [RdfTerm.Iri(Encoding.UTF8.GetBytes(Problems + type)), RdfTerm.Literal(json.WrittenSpan, RdfJson)];

    /// <summary>A term in canonical N-Triples (RDF 1.2).</summary>
    [DesignDecision(typeof(CommitTimeGatingUnderAShapesBinding.ShapesBindingProblemTypes), Scope = ExceptionScope.Boundary)]
    internal static string Term(RdfTerm term)
    {
        byte[] buffer = new byte[256];
        WriteOptions canonical = new() { Canonical = true };
        while (true)
        {
            if (NQuadsWriter.TryWriteTerm(term, buffer, out int written, in canonical))
            {
                return Encoding.UTF8.GetString(buffer, 0, written);
            }

            buffer = new byte[buffer.Length * 4];
        }
    }
}

/// <summary>Whether the refused commit changed the shapes graph too (ADR 0169's R5).</summary>
internal enum ShapesChange
{
    Unchanged,
    Changed,
}
