// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Varve.Protocol.Http;
using Varve.Protocol.Model;
using Varve.Rdf;

namespace Varve.Protocol.Endpoints;

/// <summary>
/// A dataset validator's rejection that names a problem type of its own
/// (ADR 0119 amended, ADR 0145): its report is the type's IRI and an
/// <c>rdf:JSON</c> literal of the members, <c>detail</c> among them. Any other
/// report is the caller's to answer as <c>rejected</c>.
/// </summary>
internal static class ValidatorProblem
{
    private const string RdfJson = "http://www.w3.org/1999/02/22-rdf-syntax-ns#JSON";

    /// <summary>The report of a rejection naming <paramref name="type"/> with these members.</summary>
    internal static IReadOnlyList<RdfTerm> Report(ProblemType type, byte[] membersJson) =>
    [
        RdfTerm.Iri(Encoding.UTF8.GetBytes(type.Value)),
        RdfTerm.Literal(membersJson, RdfTerm.Iri(Encoding.UTF8.GetBytes(RdfJson))),
    ];

    /// <summary>Writes the problem a report names, or answers <see langword="null"/> when it names none.</summary>
    internal static Task? TryWriteAsync(HttpContext context, IReadOnlyList<RdfTerm> report)
    {
        if (report.Count != 2
            || report[0].Kind != RdfTermKind.Iri
            || report[1].Kind != RdfTermKind.Literal
            || !report[1].DatatypeIri.SequenceEqual(Encoding.UTF8.GetBytes(RdfJson))
            || !ProblemType.TryParse(Encoding.UTF8.GetString(report[0].Lexical), out ProblemType type)
            || !ProblemCatalogue.IsValidatorType(type))
        {
            return null;
        }

        using JsonDocument members = JsonDocument.Parse(report[1].Lexical.ToArray());
        string? detail = members.RootElement.TryGetProperty("detail", out JsonElement d) ? d.GetString() : null;
        JsonElement root = members.RootElement.Clone();

        return HttpProblems.WriteAsync(context, type, detail, writer =>
        {
            foreach (JsonProperty member in root.EnumerateObject())
            {
                if (member.NameEquals("detail"))
                {
                    continue;
                }

                if (member.Value.ValueKind == JsonValueKind.String)
                {
                    writer.String(member.Name, member.Value.GetString()!);
                }
                else
                {
                    Utf8JsonWriter json = writer.Raw(member.Name);
                    json.WritePropertyName(member.Name);
                    member.Value.WriteTo(json);
                }
            }
        });
    }
}
