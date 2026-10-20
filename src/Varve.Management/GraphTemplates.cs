// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Iri;
using Varve.Management.Model;

namespace Varve.Management;

/// <summary>
/// Graph templates over the caller (ADR 0166): RFC 6570 level 1 simple string
/// expansion of <c>{sub}</c>, <c>{agent}</c> and <c>{claim.&lt;name&gt;}</c>,
/// each value percent-encoded to RFC 3986's unreserved characters.
/// </summary>
[Contract(typeof(VarveManagementAndTheVocabulary.TemplatesOverTheCaller), Role = "the expansion of a graph template over a caller")]
public static class GraphTemplates
{
    private const string Probe = "x";

    /// <summary>Why a template is refused, or none: it parses, names only known variables, and expands to an absolute IRI.</summary>
    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public static string? Check(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        string? problem = null;
        string? expanded = Expand(template, _ => Probe, ref problem);

        if (problem is not null)
        {
            return problem;
        }

        return expanded is not null && IriRef.IsAbsolute(Encoding.UTF8.GetBytes(expanded))
            ? null
            : "the template does not expand to an absolute IRI";
    }

    /// <summary>
    /// The template expanded for the caller, or none when the caller lacks a
    /// variable it names: such a caller gets nothing from the grant.
    /// </summary>
    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public static string? Expand(GraphTemplate template, Caller caller)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(caller);
        string? problem = null;
        string? expanded = Expand(template.Template, caller.Variable, ref problem);
        return problem is null ? expanded : null;
    }

    // Expands with `lookup`; null when a variable has no value. A malformed
    // template sets `problem`.
    private static string? Expand(string template, Func<string, string?> lookup, ref string? problem)
    {
        StringBuilder output = new(template.Length + 32);
        bool missing = false;
        int at = 0;

        while (at < template.Length)
        {
            char c = template[at];

            if (c == '}')
            {
                problem = "a '}' with no '{' before it";
                return null;
            }

            if (c != '{')
            {
                output.Append(c);
                at++;
                continue;
            }

            int close = template.IndexOf('}', at + 1);

            if (close < 0)
            {
                problem = "a '{' with no '}' after it";
                return null;
            }

            string name = template[(at + 1)..close];

            if (!IsVariable(name))
            {
                problem = "'{" + name + "}' is not a variable: they are {sub}, {agent} and {claim.<name>}";
                return null;
            }

            if (lookup(name) is { } value)
            {
                Encode(value, output);
            }
            else
            {
                missing = true;
            }

            at = close + 1;
        }

        return missing ? null : output.ToString();
    }

    private static bool IsVariable(string name)
    {
        if (name is "sub" or "agent")
        {
            return true;
        }

        if (!name.StartsWith("claim.", StringComparison.Ordinal) || name.Length == "claim.".Length)
        {
            return false;
        }

        foreach (char c in name.AsSpan("claim.".Length))
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.' or ':'))
            {
                return false;
            }
        }

        return true;
    }

    // RFC 6570 simple string expansion: every byte of the UTF-8 form outside
    // RFC 3986's unreserved set is percent-encoded with upper-case hex.
    private static void Encode(string value, StringBuilder output)
    {
        foreach (byte b in Encoding.UTF8.GetBytes(value))
        {
            if (b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9' or (byte)'-' or (byte)'.' or (byte)'_' or (byte)'~')
            {
                output.Append((char)b);
            }
            else
            {
                output.Append('%').Append(((int)b).ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }
}
