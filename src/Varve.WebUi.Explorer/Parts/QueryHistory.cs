// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Delta.Model;
using Varve.Protocol.Client.Model;

namespace Varve.WebUi.Explorer.Parts;

/// <summary>One query run: its text, and the dataset, regime and selector it ran with, as typed.</summary>
/// <param name="Query">The query's text.</param>
/// <param name="Dataset">The dataset it ran against.</param>
/// <param name="Regime">The regime asked for, as its header token.</param>
/// <param name="AsOf">The selector, as <see cref="QueryHistory.Write(AsOfSelector)"/> writes it.</param>
public sealed record HistoryEntry(string Query, string Dataset, string Regime, string AsOf);

/// <summary>
/// The tab's query history (ADR 0249 point 7): the last 20 queries run,
/// newest first, each once. It holds the query texts and what they ran with,
/// and no result, token, position read or request id. The page keeps it in
/// <c>sessionStorage</c>, as JSON written here without reflection, so the
/// trimmer keeps what it needs.
/// </summary>
[Contract(typeof(TheDataExplorersReads.HistoryPerTab), Role = "the last 20 queries of the tab")]
public sealed class QueryHistory
{
    /// <summary>How many queries the history keeps.</summary>
    public const int Capacity = 20;

    private readonly List<HistoryEntry> _entries;

    private QueryHistory(List<HistoryEntry> entries) => _entries = entries;

    /// <summary>The entries, newest first.</summary>
    public IReadOnlyList<HistoryEntry> Entries => _entries;

    /// <summary>
    /// The history <paramref name="json"/> holds, or an empty one for none or
    /// for anything that is not a history: storage is the viewer's, and a
    /// value someone else wrote is dropped, never trusted.
    /// </summary>
    public static QueryHistory Read(string? json)
    {
        List<HistoryEntry> entries = [];

        if (string.IsNullOrEmpty(json))
        {
            return new QueryHistory(entries);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new QueryHistory(entries);
            }

            foreach (JsonElement item in document.RootElement.EnumerateArray())
            {
                if (entries.Count < Capacity
                    && Text(item, "query") is string query
                    && Text(item, "dataset") is string dataset
                    && Text(item, "regime") is string regime
                    && Text(item, "asOf") is string asOf
                    && ReadSelector(asOf) is not null)
                {
                    entries.Add(new HistoryEntry(query, dataset, regime, asOf));
                }
            }
        }
        catch (JsonException)
        {
            entries.Clear();
        }

        return new QueryHistory(entries);
    }

    /// <summary>The history with <paramref name="entry"/> first, once, and at most <see cref="Capacity"/> entries.</summary>
    public QueryHistory With(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new QueryHistory([entry, .. _entries.Where(e => e != entry).Take(Capacity - 1)]);
    }

    /// <summary>The history as the JSON <see cref="Read(string?)"/> reads.</summary>
    public string ToJson()
    {
        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartArray();

            foreach (HistoryEntry entry in _entries)
            {
                writer.WriteStartObject();
                writer.WriteString("query", entry.Query);
                writer.WriteString("dataset", entry.Dataset);
                writer.WriteString("regime", entry.Regime);
                writer.WriteString("asOf", entry.AsOf);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>A selector as the history writes it: <c>head</c>, <c>position:p</c> or <c>time:t</c>, the header's own forms.</summary>
    public static string Write(AsOfSelector selector) => selector switch
    {
        { Position: FeedPosition position } => "position:" + position.Value.ToString(CultureInfo.InvariantCulture),
        { Time: AsOfTime time } => "time:" + time,
        _ => "head",
    };

    /// <summary>The selector <paramref name="written"/> holds, or null for none.</summary>
    public static AsOfSelector? ReadSelector(string written)
    {
        ArgumentNullException.ThrowIfNull(written);

        if (written == "head")
        {
            return AsOfSelector.Head;
        }

        if (written.StartsWith("position:", StringComparison.Ordinal)
            && long.TryParse(written.AsSpan(9), NumberStyles.None, CultureInfo.InvariantCulture, out long position))
        {
            return AsOfSelector.At(new FeedPosition(position));
        }

        if (written.StartsWith("time:", StringComparison.Ordinal)
            && DateTimeOffset.TryParse(written.AsSpan(5), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset time))
        {
            return AsOfSelector.AtTime(new AsOfTime(time));
        }

        return null;
    }

    private static string? Text(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
