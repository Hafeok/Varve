// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Varve.Sparql;
using Varve.Sparql.Algebra;

namespace Varve.Conformance.Tests;

/// <summary>What a SPARQL case produced: a tree, or the first error.</summary>
internal sealed record SparqlOutcome(AlgebraNode? Tree, SparqlParseError Error)
{
    internal bool Succeeded => Tree is not null;
}

/// <summary>
/// The parser under test, as the harness sees it: "parse this file at this
/// version", from UTF-8 and from UTF-16, and "write this tree back". A second
/// implementation would be another parser held to the same suites.
/// </summary>
internal interface ISparqlSubject
{
    SparqlOutcome Parse(SparqlManifestEntry entry, bool asUtf16);

    string Write(AlgebraNode tree);
}

internal static class SparqlSubjects
{
    internal static ISparqlSubject? Current { get; set; }
}

internal sealed class VarveSparqlSubject : ISparqlSubject
{
    [ModuleInitializer]
    internal static void Register() => SparqlSubjects.Current = new VarveSparqlSubject();

    public SparqlOutcome Parse(SparqlManifestEntry entry, bool asUtf16)
    {
        byte[] bytes = File.ReadAllBytes(entry.ActionPath);
        SparqlParseOptions options = new(Encoding.UTF8.GetBytes(entry.ActionIri), entry.Version);

        if (asUtf16)
        {
            // Strict: an invalid byte in a corpus file is a harness defect, not a case.
            string text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);

            if (entry.IsUpdate)
            {
                return SparqlParser.TryParseUpdate(text.AsSpan(), options, out Update? charUpdate, out SparqlParseError charUpdateError)
                    ? new SparqlOutcome(charUpdate, default)
                    : new SparqlOutcome(null, charUpdateError);
            }

            return SparqlParser.TryParseQuery(text.AsSpan(), options, out Query? charQuery, out SparqlParseError charQueryError)
                ? new SparqlOutcome(charQuery, default)
                : new SparqlOutcome(null, charQueryError);
        }

        if (entry.IsUpdate)
        {
            return SparqlParser.TryParseUpdate(bytes, options, out Update? update, out SparqlParseError updateError)
                ? new SparqlOutcome(update, default)
                : new SparqlOutcome(null, updateError);
        }

        return SparqlParser.TryParseQuery(bytes, options, out Query? query, out SparqlParseError queryError)
            ? new SparqlOutcome(query, default)
            : new SparqlOutcome(null, queryError);
    }

    public string Write(AlgebraNode tree) => tree switch
    {
        Query query => SparqlWriter.ToText(query),
        Update update => SparqlWriter.ToText(update),
        _ => throw new System.ArgumentException("Not a query or an update.", nameof(tree)),
    };
}
