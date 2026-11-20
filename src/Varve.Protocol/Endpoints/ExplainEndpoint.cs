// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Microsoft.AspNetCore.Http;
using Varve.Inference.Model;
using Varve.Protocol.Http;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using Varve.Turtle;

namespace Varve.Protocol.Endpoints;

/// <summary>
/// <c>GET /explain?quad=&lt;one N-Quads line&gt;</c> (ADR 0199): a quad's
/// status at the read's position, <c>explicit</c>, <c>implicit</c> with a
/// well-founded proof, or <c>absent</c>, as JSON, honouring
/// <c>Varve-As-Of</c>. A quad the caller does not read is absent. The proof
/// is the binding's closure's, through the tier-2 seam, so a projection
/// behind is <c>inference-behind</c>, a binding stopped
/// <c>inference-unavailable</c>, and a row-restricted caller is refused.
/// </summary>
internal static class ExplainEndpoint
{
    internal static async Task HandleAsync(HttpContext context, ProtocolOptions options)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            await HttpProblems.MethodNotAllowed(context, "GET, HEAD").ConfigureAwait(false);
            return;
        }

        if (await Exchange.BeginAsync(context, options, DatasetPermissions.Read).ConfigureAwait(false) is not { } exchange)
        {
            return;
        }

        string? line = context.Request.Query["quad"];
        if (string.IsNullOrWhiteSpace(line) || Parse(line) is not { } quad)
        {
            await HttpProblems.BadRequest(context, "Explain names one quad as quad=<one N-Quads line>, with no blank node.").ConfigureAwait(false);
            return;
        }

        if (await Reads.ResolveAsync(context, exchange.Dataset).ConfigureAwait(false) is not Position position)
        {
            return;
        }

        DatasetView? opened = await Reads.OpenAsync(context, exchange.Dataset, position, Reads.IsAsOf(context), options.Limits, context.RequestAborted).ConfigureAwait(false);
        if (opened is not { } view)
        {
            return;
        }

        context.Response.RegisterForDispose(view);
        Preconditions.Describe(context.Response, view.Position);
        IReadOnlyList<IInferenceClosure> closures = options.InferenceClosures?.Of(exchange.Name) ?? [];
        Explanation explanation;

        try
        {
            // Through the seam: a row-restricted caller is refused tier 2 (ADR 0238 point 2).
            _ = exchange.Readable(view, closures.Count > 0 ? ClosureMode.Union : ClosureMode.Plain);
            explanation = Explain(exchange, view, closures, quad);
        }
        catch (EntailmentNotAvailableException refused)
        {
            await HttpProblems.WriteAsync(context, ProblemType.EntailmentNotAvailable, refused.Message).ConfigureAwait(false);
            return;
        }
        catch (InferenceBehindException behind)
        {
            context.Response.Headers.RetryAfter = "1";
            await HttpProblems.WriteAsync(context, ProblemType.InferenceBehind, behind.Message, BoundedReads.InferenceBehind(behind)).ConfigureAwait(false);
            return;
        }
        catch (InferenceUnavailableException stopped)
        {
            await HttpProblems.WriteAsync(context, ProblemType.InferenceUnavailable, stopped.Message, BoundedReads.InferenceUnavailable(stopped)).ConfigureAwait(false);
            return;
        }

        context.Response.ContentType = "application/json";
        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        ArrayBufferWriter<byte> body = new();
        using (Utf8JsonWriter json = new(body))
        {
            json.WriteStartObject();
            json.WriteString("quad", Line(quad.S, quad.P, quad.O, quad.G));
            json.WriteNumber("position", view.Position.Value);
            ExplainedQuad? proof = explanation.Proof;
            json.WriteString("status", proof is null ? "absent" : Status(proof.Status));
            if (proof is { Status: ProofStatus.Implicit })
            {
                json.WritePropertyName("proof");
                Body(json, proof);
            }

            if (explanation.Clashes.Count > 0)
            {
                json.WriteBoolean("inconsistent", true);
                json.WriteStartArray("clashes");
                foreach (ExplainedClash clash in explanation.Clashes)
                {
                    json.WriteStartObject();
                    json.WriteString("rule", clash.Rule.Lexical);
                    Premises(json, clash.Premises);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
            }

            json.WriteEndObject();
        }

        await context.Response.Body.WriteAsync(body.WrittenMemory, context.RequestAborted).ConfigureAwait(false);
    }

    private static Explanation Explain(Exchange exchange, DatasetView view, IReadOnlyList<IInferenceClosure> closures, (RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm? G) quad)
    {
        GraphScope readable = exchange.Scope.Readable;
        foreach (IInferenceClosure closure in closures)
        {
            // The binding's TBox conclusions: read by every reader of the dataset (ADR 0190 point 3).
            if (quad.G is { } named && named.Equals(closure.TBoxConclusions))
            {
                return readable.IsEmpty ? new Explanation(null, []) : closure.Explain(view.Position, quad.G, quad.S, quad.P, quad.O);
            }
        }

        // A quad the caller does not see is absent, as if it were not entailed (ADR 0199 point 4).
        if (quad.G is null ? !readable.IncludesDefault : !readable.Allows(quad.G))
        {
            return new Explanation(null, []);
        }

        foreach (IInferenceClosure closure in closures)
        {
            if (Covers(closure, quad.G))
            {
                return closure.Explain(view.Position, quad.G, quad.S, quad.P, quad.O);
            }
        }

        // A graph no materialising binding covers has no implicit part: explicit, or absent.
        bool asserted = view.TryInternalise(quad.S, out TermHandle s) && view.TryInternalise(quad.P, out TermHandle p) && view.TryInternalise(quad.O, out TermHandle o)
            && (quad.G is null ? view.Contains(new Quad(s, p, o)) : view.TryInternalise(quad.G, out TermHandle g) && view.Contains(new Quad(s, p, o, g)));
        return new Explanation(asserted ? new ExplainedQuad(quad.S, quad.P, quad.O, quad.G, ProofStatus.Explicit, null, []) : null, []);
    }

    /// <summary>Whether a graph is one of a binding's data graphs: in its scope, not a TBox graph, not under <c>varve:</c>.</summary>
    private static bool Covers(IInferenceClosure closure, RdfTerm? graph)
    {
        if (graph is null)
        {
            return closure.DataGraphs.IncludesDefault;
        }

        foreach (RdfTerm tbox in closure.TBoxGraphs)
        {
            if (tbox.Equals(graph))
            {
                return false;
            }
        }

        return !graph.Lexical.StartsWith("https://w3id.org/varve/ns#"u8) && closure.DataGraphs.Allows(graph);
    }

    private static void Body(Utf8JsonWriter json, ExplainedQuad node)
    {
        json.WriteStartObject();
        if (node.Rule is { } rule)
        {
            json.WriteString("rule", rule.Lexical);
        }

        Premises(json, node.Premises);
        json.WriteEndObject();
    }

    private static void Premises(Utf8JsonWriter json, IReadOnlyList<ExplainedQuad> premises)
    {
        json.WriteStartArray("premises");
        foreach (ExplainedQuad premise in premises)
        {
            json.WriteStartObject();
            json.WriteString("quad", Line(premise.Subject, premise.Predicate, premise.Object, premise.Graph));
            json.WriteString("status", Status(premise.Status));
            if (premise.Graph is { } graph)
            {
                json.WriteString("graph", graph.Lexical);
            }

            if (premise.Status == ProofStatus.Implicit)
            {
                json.WritePropertyName("proof");
                Body(json, premise);
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static string Status(ProofStatus status) => status switch
    {
        ProofStatus.Explicit => "explicit",
        ProofStatus.Axiom => "axiom",
        _ => "implicit",
    };

    /// <summary>The one quad of an N-Quads line, with no blank node; null for anything else.</summary>
    private static (RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm? G)? Parse(string line)
    {
        List<(RdfTerm, RdfTerm, RdfTerm, RdfTerm?)> quads = [];
        ParseResult result = NQuadsParser.Parse(
            Encoding.UTF8.GetBytes(line.Trim() + "\n"),
            (in QuadView quad) => quads.Add((quad.Subject.Materialise(), quad.Predicate.Materialise(), quad.Object.Materialise(), quad.HasGraph ? quad.Graph.Materialise() : null)),
            new ParseOptions { Syntax = RdfSyntax.NQuads });
        if (!result.Succeeded || quads.Count != 1)
        {
            return null;
        }

        (RdfTerm s, RdfTerm p, RdfTerm o, RdfTerm? g) = quads[0];
        return s.Kind == RdfTermKind.BlankNode || o.Kind == RdfTermKind.BlankNode || g is { Kind: RdfTermKind.BlankNode } ? null : (s, p, o, g);
    }

    /// <summary>A quad as one N-Quads line.</summary>
    [DesignDecision(typeof(Explain.ExplainRoute), Scope = ExceptionScope.Boundary)]
    private static string Line(RdfTerm subject, RdfTerm predicate, RdfTerm @object, RdfTerm? graph)
    {
        StringBuilder text = new();
        foreach (RdfTerm? term in (RdfTerm?[])[subject, predicate, @object, graph])
        {
            if (term is null)
            {
                continue;
            }

            byte[] buffer = new byte[256 + (4 * term.Lexical.Length) + (term.Kind == RdfTermKind.Literal ? 4 * term.DatatypeIri.Length : 0)];
            while (!NQuadsWriter.TryWriteTerm(term, buffer, out int written, default))
            {
                buffer = new byte[buffer.Length * 2];
                _ = written;
            }

            NQuadsWriter.TryWriteTerm(term, buffer, out int length, default);
            text.Append(Encoding.UTF8.GetString(buffer, 0, length)).Append(' ');
        }

        return text.Append('.').ToString();
    }
}
