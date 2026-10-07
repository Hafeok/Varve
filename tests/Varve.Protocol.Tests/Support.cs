// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using Varve.Turtle;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>A clock the test moves; the store, the limits and the evaluator read it.</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public static ManualClock Epoch() => new(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));
}

internal static class P
{
    internal static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static ValueTask<Dataset> NewDatasetAsync(TimeProvider? clock = null) =>
        Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = clock ?? TimeProvider.System }, Ct);

    internal static HttpRequestMessage Update(string path, string text, string? ifMatch = null)
    {
        HttpRequestMessage request = new(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(text, Encoding.UTF8, "application/sparql-update"),
        };

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return request;
    }

    internal static HttpRequestMessage Query(string path, string text, string accept = "application/sparql-results+json", string? asOf = null)
    {
        HttpRequestMessage request = new(HttpMethod.Get, new Uri(path + "?query=" + Uri.EscapeDataString(text), UriKind.Relative));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));

        if (asOf is not null)
        {
            request.Headers.TryAddWithoutValidation("Varve-As-Of", asOf);
        }

        return request;
    }

    internal static HttpRequestMessage Get(string path, string? accept = null)
    {
        HttpRequestMessage request = new(HttpMethod.Get, new Uri(path, UriKind.Relative));

        if (accept is not null)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        }

        return request;
    }

    internal static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out IEnumerable<string>? values) ? string.Join(",", values) : null;

    /// <summary>Every record of a bounded feed or a diff.</summary>
    internal static async Task<List<FeedRecord>> ReadFeedAsync(HttpResponseMessage response)
    {
        List<FeedRecord> records = [];
        using Stream body = await response.Content.ReadAsStreamAsync(Ct);

        await foreach (FeedRecord record in ChangeFeedReader.ReadAllAsync(body, Ct))
        {
            records.Add(record);
        }

        return records;
    }

    /// <summary>The quads of a view as canonical N-Quads lines, blank nodes by their stable labels.</summary>
    internal static SortedSet<string> Lines(DatasetView view)
    {
        SortedSet<string> lines = new(StringComparer.Ordinal);
        using IQuadCursor cursor = view.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);

        while (cursor.MoveNext())
        {
            Quad quad = cursor.Current;
            lines.Add(Line(Term(view, quad.Subject), Term(view, quad.Predicate), Term(view, quad.Object), quad.Graph.IsNone ? null : Term(view, quad.Graph)));
        }

        return lines;
    }

    /// <summary>A change as the line that names its quad.</summary>
    internal static string Line(FeedChange change) => Line(change.Subject, change.Predicate, change.Object, change.Graph);

    internal static string Line(RdfTerm subject, RdfTerm predicate, RdfTerm @object, RdfTerm? graph)
    {
        StringBuilder text = new();
        text.Append(TermText(subject)).Append(' ').Append(TermText(predicate)).Append(' ').Append(TermText(@object));

        if (graph is not null)
        {
            text.Append(' ').Append(TermText(graph));
        }

        return text.ToString();
    }

    internal static string TermText(RdfTerm term)
    {
        byte[] buffer = new byte[4096];
        Assert.True(NQuadsWriter.TryWriteTerm(term, buffer, out int written, new WriteOptions { Syntax = RdfSyntax.NQuads, Canonical = true }));
        return Encoding.UTF8.GetString(buffer, 0, written);
    }

    /// <summary>Applies a feed's records to a set of lines: the replay of change-feed.md §6.</summary>
    internal static void Apply(SortedSet<string> state, IEnumerable<FeedRecord> records)
    {
        foreach (FeedRecord record in records)
        {
            foreach (FeedChange change in record.Changes)
            {
                if (change.Kind == FeedChangeKind.Assert)
                {
                    Assert.True(state.Add(Line(change)), "an assertion of a quad already present: " + Line(change));
                }
                else
                {
                    Assert.True(state.Remove(Line(change)), "a retraction of a quad not present: " + Line(change));
                }
            }
        }
    }

    private static RdfTerm Term(DatasetView view, TermHandle handle)
    {
        Assert.True(view.TryExternalise(handle, out RdfTerm? term));
        return term!;
    }
}
