// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Protocol;

/// <summary>
/// The client idempotency keys a writer honours (ADR 0246): for each dataset
/// it writes, the keyed commits among the last <see cref="Window"/> commits
/// of its log, by agent and key. Built from the log — each keyed commit
/// carries its key as a host attachment — so a retry after a restart, a
/// handover or a forward finds the key the same way. Never bounded by time.
/// </summary>
[Contract(typeof(ClientIdempotencyKeys.KeysKeptForAWindowOfCommits), Role = "the writer's keys, read from the log over a window of commits")]
public sealed class IdempotencyKeys
{
    private readonly ConditionalWeakTable<Dataset, KeyIndex> indexes = [];

    /// <summary>A writer's keys over <see cref="CommitWindow.Default"/>.</summary>
    public IdempotencyKeys()
        : this(CommitWindow.Default)
    {
    }

    /// <summary>A writer's keys over <paramref name="window"/>.</summary>
    public IdempotencyKeys(CommitWindow window) => Window = window;

    /// <summary>
    /// How many of a dataset's last commits a key is honoured for. A key whose
    /// commit is older is forgotten, and a retry with it commits again. A
    /// function of the log alone, so every writer of the dataset agrees.
    /// </summary>
    public CommitWindow Window { get; }

    /// <summary>The index of <paramref name="dataset"/>: one per open <see cref="Dataset"/>, so that a reopened dataset reads its log afresh.</summary>
    internal KeyIndex IndexOf(Dataset dataset) => indexes.GetValue(dataset, _ => new KeyIndex(Window.Commits));
}

/// <summary>One agent's key: who sent it and the key itself (ADR 0246). The agent is the commit's agent IRI, or empty in anonymous mode.</summary>
internal sealed record AgentKey(string Agent, string Key);

/// <summary>What a keyed write answers when it commits, and a retry of it is answered again: the status, <c>201</c> or <c>204</c>, and the <c>Location</c>.</summary>
internal sealed record KeyedAnswer(int Status, string? Location)
{
    /// <summary><c>204</c>, with no <c>Location</c>: what most writes answer.</summary>
    internal static KeyedAnswer NoContent => new(204, null);
}

/// <summary>What a keyed commit recorded: where it is, what was sent, what it answered, and its request id.</summary>
internal sealed record KeyedCommit(long Position, string Fingerprint, KeyedAnswer Answer, string? RequestId);

/// <summary>
/// The outcome of looking a key up and, when it is free, claiming it for one
/// request: the claim, the commit that already holds the key, or a request
/// still in flight with it.
/// </summary>
internal sealed record KeyLookup(KeyedCommit? Committed, bool InFlight, KeyClaim? Claim);

/// <summary>
/// A key one request holds while it commits. Settled once, with the commit
/// or with nothing; disposing an unsettled claim releases the key.
/// </summary>
internal sealed class KeyClaim : IDisposable
{
    private readonly KeyIndex _index;
    private int _settled;

    internal KeyClaim(KeyIndex index, AgentKey key, string fingerprint)
    {
        _index = index;
        Key = key;
        Fingerprint = fingerprint;
    }

    internal AgentKey Key { get; }

    internal string Fingerprint { get; }

    /// <summary>What the commit answers, recorded with it.</summary>
    internal KeyedAnswer Answer { get; set; } = KeyedAnswer.NoContent;

    /// <summary>The commit landed: kept with what it recorded.</summary>
    internal void Committed(KeyedCommit commit)
    {
        if (Interlocked.Exchange(ref _settled, 1) == 0)
        {
            _index.Settle(this, commit);
        }
    }

    /// <summary>Nothing was committed: the key is free again.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _settled, 1) == 0)
        {
            _index.Settle(this, null);
        }
    }
}

/// <summary>
/// The keys one open dataset's log records over the window, and the keys
/// requests on this instance hold in flight. Catching up reads the commits
/// after the last it read; a request holds its key from before it is
/// evaluated until it is answered, so two requests with one key never reach
/// the sequencer together on one writer.
/// </summary>
internal sealed class KeyIndex(long window)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<AgentKey, KeyedCommit> _committed = [];
    private readonly HashSet<AgentKey> _inFlight = [];
    private readonly Queue<(long Position, AgentKey Key)> _order = new();
    private long _readTo;

    /// <summary>
    /// Reads the commits of <paramref name="dataset"/> after the last read,
    /// within the window, and adds the keys they record. Every keyed commit
    /// at or below the head is then known, whoever wrote it.
    /// </summary>
    internal async ValueTask CatchUpAsync(Dataset dataset, CancellationToken cancellationToken)
    {
        // Two requests may read the same commits at once: adding a key is
        // idempotent, and the position read to only rises.
        long head = dataset.Head.Value;
        long from = Math.Max(Volatile.Read(ref _readTo), head - window);

        if (from >= head)
        {
            return;
        }

        await foreach (Commit commit in dataset.Subscribe(new Position(from), SubscriptionFilter.All, cancellationToken).ConfigureAwait(false))
        {
            if (KeyAttachment.Read(commit) is { } recorded)
            {
                lock (_gate)
                {
                    Add(recorded.Key, recorded.Commit);
                }
            }

            if (commit.Position.Value >= head)
            {
                break;
            }
        }

        long seen = Volatile.Read(ref _readTo);

        while (head > seen && Interlocked.CompareExchange(ref _readTo, head, seen) is long found && found != seen)
        {
            seen = found;
        }

        Evict(head);
    }

    /// <summary>
    /// The commit that holds <paramref name="key"/>; or, when none does and no
    /// request holds it, a claim on it for this request.
    /// </summary>
    internal KeyLookup Lookup(AgentKey key, string fingerprint)
    {
        lock (_gate)
        {
            if (_inFlight.Contains(key))
            {
                return new KeyLookup(null, InFlight: true, null);
            }

            if (_committed.TryGetValue(key, out KeyedCommit? committed))
            {
                return new KeyLookup(committed, InFlight: false, null);
            }

            _inFlight.Add(key);
            return new KeyLookup(null, InFlight: false, new KeyClaim(this, key, fingerprint));
        }
    }

    internal void Settle(KeyClaim claim, KeyedCommit? committed)
    {
        lock (_gate)
        {
            _inFlight.Remove(claim.Key);

            if (committed is not null)
            {
                Add(claim.Key, committed);
            }
        }
    }

    // A key is recorded once, at the first position that carries it: the
    // writer never lets a second commit carry it while the first is in the
    // window.
    private void Add(AgentKey key, KeyedCommit commit)
    {
        if (_committed.TryAdd(key, commit))
        {
            _order.Enqueue((commit.Position, key));
        }
    }

    // Keys whose commits fell out of the window are forgotten (ADR 0246).
    private void Evict(long head)
    {
        lock (_gate)
        {
            while (_order.TryPeek(out (long Position, AgentKey Key) oldest) && oldest.Position <= head - window)
            {
                _order.Dequeue();

                if (_committed.TryGetValue(oldest.Key, out KeyedCommit? held) && held.Position == oldest.Position)
                {
                    _committed.Remove(oldest.Key);
                }
            }
        }
    }
}

/// <summary>
/// The host attachment a keyed commit carries (ADR 0246): the literal
/// <c>"{…}"^^varve:idempotencyKey</c>, its lexical form the canonical JSON
/// (RFC 8785) of the key, the request's fingerprint, the status the write
/// answered and its <c>Location</c>. The agent is the commit's own.
/// </summary>
internal static class KeyAttachment
{
    /// <summary>The datatype of the attachment.</summary>
    internal const string Datatype = "https://w3id.org/varve/ns#idempotencyKey";

    /// <summary>The attachment a claim's commit records.</summary>
    internal static RdfTerm Of(KeyClaim claim)
    {
        System.Buffers.ArrayBufferWriter<byte> json = new();
        Json.Canonical.CanonicalJsonWriter writer = new(json);
        writer.StartObject();
        writer.WriteName("fingerprint"u8);
        writer.WriteString(claim.Fingerprint);
        writer.WriteName("key"u8);
        writer.WriteString(claim.Key.Key);

        if (claim.Answer.Location is { } location)
        {
            writer.WriteName("location"u8);
            writer.WriteString(location);
        }

        writer.WriteName("status"u8);
        writer.WriteNumber(claim.Answer.Status);
        writer.EndObject();
        return RdfTerm.Literal(json.WrittenSpan.ToArray(), RdfTerm.Iri(Encoding.UTF8.GetBytes(Datatype)));
    }

    /// <summary>The key a commit records, with its agent, when it records one.</summary>
    internal static (AgentKey Key, KeyedCommit Commit)? Read(Commit commit)
    {
        foreach (TermHandle handle in commit.Attachments.Span)
        {
            if (!commit.TryExternalise(handle, out RdfTerm? term)
                || term.Kind != RdfTermKind.Literal
                || !term.DatatypeIri.SequenceEqual("https://w3id.org/varve/ns#idempotencyKey"u8)
                || !TryParse(term.Lexical, out string? key, out string? fingerprint, out int status, out string? location))
            {
                continue;
            }

            string agent = !commit.Agent.IsNone && commit.TryExternalise(commit.Agent, out RdfTerm? who) ? Encoding.UTF8.GetString(who.Lexical) : string.Empty;
            string? cause = !commit.Cause.IsNone && commit.TryExternalise(commit.Cause, out RdfTerm? why) ? Encoding.UTF8.GetString(why.Lexical) : null;
            return (new AgentKey(agent, key), new KeyedCommit(commit.Position.Value, fingerprint, new KeyedAnswer(status, location), cause));
        }

        return null;
    }

    private static bool TryParse(ReadOnlySpan<byte> lexical, out string key, out string fingerprint, out int status, out string? location)
    {
        key = string.Empty;
        fingerprint = string.Empty;
        status = 204;
        location = null;

        try
        {
            System.Text.Json.Utf8JsonReader reader = new(lexical);

            if (!reader.Read() || reader.TokenType != System.Text.Json.JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read() && reader.TokenType == System.Text.Json.JsonTokenType.PropertyName)
            {
                string name = reader.GetString()!;

                if (!reader.Read())
                {
                    return false;
                }

                switch (name)
                {
                    case "key":
                        key = reader.GetString() ?? string.Empty;
                        break;
                    case "fingerprint":
                        fingerprint = reader.GetString() ?? string.Empty;
                        break;
                    case "location":
                        location = reader.GetString();
                        break;
                    case "status":
                        status = reader.GetInt32();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return key.Length > 0 && fingerprint.Length > 0;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
