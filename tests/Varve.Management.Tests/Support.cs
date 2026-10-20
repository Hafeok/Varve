// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Varve.Management.Model;
using Varve.Rdf;
using Xunit;

namespace Varve.Management.Tests;

internal static class T
{
    public const string Prefixes = "@prefix varve: <https://w3id.org/varve/ns#> .\n@prefix xsd: <http://www.w3.org/2001/XMLSchema#> .\n@prefix n: <https://notes.example/manage/> .\n";

    public static InMemoryDataset Trig(string text)
    {
        InMemoryDataset? dataset = ManagementTrig.TryParse(Encoding.UTF8.GetBytes(Prefixes + text), null, out string? error);
        Assert.True(dataset is not null, error);
        return dataset!;
    }

    public static Declarations Read(string text) => DeclarationReader.Read(Trig(text));

    public static Caller Caller(string? agent = null, string? sub = null, string[]? roles = null, (string Type, string Value)[]? claims = null)
    {
        Dictionary<string, IReadOnlyList<string>> all = [];

        if (roles is not null)
        {
            all["roles"] = roles;
        }

        foreach (IGrouping<string, (string Type, string Value)> group in (claims ?? []).GroupBy(claim => claim.Type))
        {
            all[group.Key] = [.. group.Select(claim => claim.Value)];
        }

        return new Caller(agent, sub, all, "roles");
    }

    public static RdfTerm Iri(string iri) => RdfTerm.Iri(Encoding.UTF8.GetBytes(iri));
}
