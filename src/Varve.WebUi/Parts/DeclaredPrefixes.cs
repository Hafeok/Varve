// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Varve.WebUi.Parts;

/// <summary>
/// The prefixes a declaration defines in its own TriG, as served (review
/// F13): a declaration's IRIs are shown compact only with these, never with
/// a prefix the console knows and the declaration does not, and an agent is
/// never compacted.
/// </summary>
public sealed partial class DeclaredPrefixes
{
    private readonly List<(string Prefix, string Namespace)> _prefixes;

    private DeclaredPrefixes(List<(string Prefix, string Namespace)> prefixes) => _prefixes = prefixes;

    /// <summary>No prefixes: every IRI shown in full.</summary>
    public static DeclaredPrefixes None { get; } = new([]);

    /// <summary>The <c>@prefix</c> and <c>PREFIX</c> directives of <paramref name="trig"/>.</summary>
    public static DeclaredPrefixes Of(string trig)
    {
        List<(string, string)> prefixes = [];

        foreach (Match directive in Directive().Matches(trig))
        {
            prefixes.Add((directive.Groups["prefix"].Value, directive.Groups["namespace"].Value));
        }

        return new DeclaredPrefixes(prefixes);
    }

    /// <summary>The compact form of <paramref name="iri"/>, or null when no declared prefix applies.</summary>
    public string? Compact(string iri)
    {
        foreach ((string prefix, string ns) in _prefixes)
        {
            if (ns.Length > 0 && iri.Length > ns.Length && iri.StartsWith(ns, StringComparison.Ordinal))
            {
                return prefix + ":" + iri[ns.Length..];
            }
        }

        return null;
    }

    [GeneratedRegex(@"(?im)^\s*(?:@prefix|PREFIX)\s+(?<prefix>[^\s:]*):\s*<(?<namespace>[^>]*)>")]
    private static partial Regex Directive();
}
