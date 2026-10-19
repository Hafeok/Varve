// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;
using Varve.Management;

namespace Varve.Server;

/// <summary>
/// The configuration the management plane replaced (ADR 0163), read as it was
/// written and printed as the declarations that replace it: each
/// <c>Varve:Datasets:{name}</c> a dataset's graph, each
/// <c>Varve:Auth:Datasets:{name}</c> its grants, and
/// <c>Varve:Auth:Server:Admin</c> the bootstrap operators. This is
/// <c>varve apply --from-config</c>'s.
/// </summary>
internal static class LegacyConfiguration
{
    private const string Prefix = "urn:varve:from-config:";

    /// <summary>Whether a <c>Varve:</c> key is one the management plane replaced.</summary>
    internal static bool IsLegacy(string key) =>
        key.StartsWith("Varve:Datasets:", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("Varve:Auth:Datasets:", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("Varve:Auth:Server:", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The TriG of the datasets and grants <paramref name="varve"/>, the
    /// <c>Varve</c> section, configures, and the bootstrap operators its
    /// server admins become; errors for what does not convert.
    /// </summary>
    internal static (string Trig, List<string> Bootstrap, List<string> Errors) Convert(IConfigurationSection varve)
    {
        StringBuilder trig = new();
        List<string> errors = [];
        List<string> bootstrap = [];
        trig.Append("@prefix varve: <").Append(ManagementVocabulary.Namespace).Append("> .\n");
        SortedSet<string> names = new(StringComparer.Ordinal);

        foreach (IConfigurationSection dataset in varve.GetSection("Datasets").GetChildren())
        {
            names.Add(dataset.Key);
        }

        foreach (IConfigurationSection dataset in varve.GetSection("Auth:Datasets").GetChildren())
        {
            names.Add(dataset.Key);
        }

        foreach (string name in names)
        {
            if (!ManagementVocabulary.IsDatasetName(name))
            {
                errors.Add(name + " is not a dataset name: 1 to 63 characters of [A-Za-z0-9._-], starting with a letter or a digit (ADR 0093).");
                continue;
            }

            string storage = varve["Datasets:" + name + ":Storage"] ?? "File";

            if (storage is not ("File" or "Memory" or "ObjectStorage"))
            {
                errors.Add("Varve:Datasets:" + name + ":Storage is File, Memory or ObjectStorage, not " + storage + ".");
                continue;
            }

            string node = Iri(Prefix + name);
            trig.Append('\n').Append(Iri(ManagementVocabulary.DatasetGraphPrefix + name)).Append(" {\n");
            trig.Append("  ").Append(node).Append(" a varve:Dataset ;\n");
            trig.Append("    varve:datasetName ").Append(Literal(name)).Append(" ;\n");
            trig.Append("    varve:storage varve:").Append(storage).Append(" .\n");
            Grants(trig, varve.GetSection("Auth:Datasets:" + name), name, errors);
            trig.Append("}\n");
        }

        foreach (IConfigurationSection admin in varve.GetSection("Auth:Server:Admin").GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(admin.Value))
            {
                bootstrap.Add(admin.Value);
            }
        }

        return (trig.ToString(), bootstrap, errors);
    }

    // A role per claim value, and a grant per value and permission; a scoped
    // grant (ADR 0107) keeps its graphs and prefixes.
    private static void Grants(StringBuilder trig, IConfigurationSection permissions, string dataset, List<string> errors)
    {
        Dictionary<string, string> roles = new(StringComparer.Ordinal);
        int count = 0;

        string Role(string value)
        {
            if (!roles.TryGetValue(value, out string? role))
            {
                role = Iri(Prefix + dataset + ":role:" + roles.Count.ToString(CultureInfo.InvariantCulture));
                roles[value] = role;
                trig.Append("  ").Append(role).Append(" a varve:Role ; varve:claimValue ").Append(Literal(value)).Append(" .\n");
            }

            return role;
        }

        string Grant(string role, string permission)
        {
            string grant = Iri(Prefix + dataset + ":grant:" + (count++).ToString(CultureInfo.InvariantCulture));
            trig.Append("  ").Append(grant).Append(" a varve:Grant ; varve:grantee ").Append(role).Append(" ; varve:permission varve:").Append(permission);
            return grant;
        }

        foreach (string permission in (ReadOnlySpan<string>)["Read", "Write", "Admin"])
        {
            foreach (IConfigurationSection value in permissions.GetSection(permission).GetChildren())
            {
                if (string.IsNullOrWhiteSpace(value.Value))
                {
                    continue;
                }

                Grant(Role(value.Value), permission.ToLowerInvariant());
                trig.Append(permission == "Admin" ? " .\n" : " ; varve:allGraphs true .\n");
            }
        }

        foreach (IConfigurationSection scoped in permissions.GetSection("Grants").GetChildren())
        {
            string at = "Varve:Auth:Datasets:" + dataset + ":Grants:" + scoped.Key;
            string? claim = scoped["Claim"];
            string? permission = scoped["Permission"];

            if (string.IsNullOrWhiteSpace(claim) || permission is not ("read" or "write"))
            {
                errors.Add(at + " names a Claim, and a Permission of read or write.");
                continue;
            }

            Grant(Role(claim), permission);
            bool any = false;

            foreach (IConfigurationSection graph in scoped.GetSection("Graphs").GetChildren())
            {
                trig.Append(" ;\n    varve:graph ").Append(graph.Value == "default" ? "varve:defaultGraph" : Iri(graph.Value ?? string.Empty));
                any = true;
            }

            foreach (IConfigurationSection prefix in scoped.GetSection("GraphPrefixes").GetChildren())
            {
                trig.Append(" ;\n    varve:graphPrefix ").Append(Literal(prefix.Value ?? string.Empty));
                any = true;
            }

            if (!any)
            {
                errors.Add(at + " names at least one graph or graph prefix.");
            }

            trig.Append(" .\n");
        }
    }

    private static string Iri(string value)
    {
        StringBuilder iri = new(value.Length + 2);
        iri.Append('<');

        foreach (char c in value)
        {
            // IRIREF excludes these; a configured IRI holding one is escaped
            // rather than allowed to end the term.
            if (c is '<' or '>' or '"' or '{' or '}' or '|' or '^' or '`' or '\\' || c <= ' ')
            {
                iri.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }
            else
            {
                iri.Append(c);
            }
        }

        return iri.Append('>').ToString();
    }

    private static string Literal(string value)
    {
        StringBuilder literal = new(value.Length + 2);
        literal.Append('"');

        foreach (char c in value)
        {
            switch (c)
            {
                case '"':
                    literal.Append("\\\"");
                    break;
                case '\\':
                    literal.Append("\\\\");
                    break;
                case '\n':
                    literal.Append("\\n");
                    break;
                case '\r':
                    literal.Append("\\r");
                    break;
                default:
                    literal.Append(c);
                    break;
            }
        }

        return literal.Append('"').ToString();
    }
}
