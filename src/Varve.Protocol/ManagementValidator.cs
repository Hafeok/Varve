// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Json.Canonical;
using Varve.Management;
using Varve.Management.Model;
using Varve.Protocol.Endpoints;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Protocol;

/// <summary>
/// The code validator of the system dataset (ADR 0166 point 4): every commit
/// to it is read as declarations, and a finding in a graph the commit touches
/// refuses it as <c>declaration-invalid</c>, naming the graph, the focus node
/// and the path. A quad outside the fleet's and the datasets' graphs is
/// refused, and so is any change to the bootstrap graph, which only the
/// system writer records, and not through this validator.
/// </summary>
[Contract(typeof(VarveManagementAndTheVocabulary.CodeValidatorNamesFocusAndPath), Role = "the system dataset's validator of management declarations")]
public sealed class ManagementValidator : ICommitValidator
{
    /// <inheritdoc />
    public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        HashSet<RdfTerm> touched = [];
        List<DeclarationFinding> findings = [];
        Touch(proposed, delta.Asserted, touched, findings);
        Touch(proposed, delta.Retracted, touched, findings);

        if (findings.Count == 0 && touched.Count > 0)
        {
            foreach (DeclarationFinding finding in DeclarationReader.Read(proposed).Findings)
            {
                if (touched.Contains(finding.Graph))
                {
                    findings.Add(finding);
                }
            }
        }

        return findings.Count == 0 ? ValidationVerdict.Accept() : ValidationVerdict.Reject(Report(findings));
    }

    private static void Touch(IQuadSource proposed, ReadOnlySpan<Quad> quads, HashSet<RdfTerm> touched, List<DeclarationFinding> findings)
    {
        foreach (Quad quad in quads)
        {
            if (quad.IsDefaultGraph || !proposed.TryExternalise(quad.Graph, out RdfTerm? graph))
            {
                findings.Add(new DeclarationFinding(RdfTerm.Iri("urn:x-varve:default-graph"u8), null, null,
                    "the system dataset's default graph holds nothing: every declaration is in a management graph"));
                return;
            }

            if (graph.Equals(ManagementVocabulary.BootstrapGraph))
            {
                findings.Add(new DeclarationFinding(graph, null, null, "the bootstrap graph is recorded by the system writer alone (ADR 0163 point 9)"));
                return;
            }

            if (!ManagementVocabulary.IsManagementGraph(graph))
            {
                findings.Add(new DeclarationFinding(graph, null, null, "the system dataset holds the fleet graph and the datasets' graphs, and nothing else"));
                return;
            }

            touched.Add(graph);
        }
    }

    private static IReadOnlyList<RdfTerm> Report(List<DeclarationFinding> findings)
    {
        ArrayBufferWriter<byte> json = new();
        CanonicalJsonWriter writer = new(json);
        writer.StartObject();
        writer.WriteName("detail"u8);
        writer.WriteString(findings[0].Message);
        writer.WriteName("findings"u8);
        writer.StartArray();

        foreach (DeclarationFinding finding in findings)
        {
            writer.StartObject();

            if (finding.Focus is { } focus)
            {
                writer.WriteName("focus"u8);
                writer.WriteString(focus.Lexical);
            }

            writer.WriteName("graph"u8);
            writer.WriteString(finding.Graph.Lexical);
            writer.WriteName("message"u8);
            writer.WriteString(finding.Message);

            if (finding.Path is { } path)
            {
                writer.WriteName("path"u8);
                writer.WriteString(path.Lexical);
            }

            writer.EndObject();
        }

        writer.EndArray();
        writer.EndObject();
        return ValidatorProblem.Report(ProblemType.DeclarationInvalid, json.WrittenSpan.ToArray());
    }
}
