// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
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
/// of its log. Each keyed commit carries a hash of its agent and key, never
/// the key, as a host attachment, and the index is read from the log, so a
/// retry after a restart, a handover or a forward finds it the same way.
/// Never bounded by time.
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
    /// commit is older is forgotten, and a retry with it is refused when the
    /// client says when it made the key (ADR 0246). A function of the log
    /// alone, so every writer of the dataset agrees.
    /// </summary>
    public CommitWindow Window { get; }

    /// <summary>
    /// Reads the keys of <paramref name="dataset"/>'s window from its log, up
    /// to its head, on the thread pool: what the first keyed write would
    /// otherwise do. A host starts it when it becomes the dataset's writer
    /// and does not wait for it, so neither the open nor a takeover is made
    /// longer; a keyed write that arrives before it finishes waits for it.
    /// Started once per open dataset: a second call answers the first's task.
    /// </summary>
    public ValueTask PrepareAsync(Dataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return new ValueTask(IndexOf(dataset).WarmAsync(dataset));
    }

    /// <summary>
    /// What a warm-up waits for before it reads: completed, except where a
    /// test holds it open to show that nothing else waits for the warm-up.
    /// </summary>
    internal Task WarmGate { get; set; } = Task.CompletedTask;

    /// <summary>The index of <paramref name="dataset"/>: one per open <see cref="Dataset"/>, so that a reopened dataset reads its log afresh.</summary>
    internal KeyIndex IndexOf(Dataset dataset) => indexes.GetValue(dataset, _ => new KeyIndex(Window.Commits, () => WarmGate));
}

/// <summary>
/// SHA-256 over the agent's IRI, a NUL, and the key (ADR 0246): what a
/// keyed commit records in place of the key. The agent is empty in anonymous
/// mode. NUL occurs in neither an IRI nor a key, so two pairs never hash
/// alike but by collision.
/// </summary>
internal readonly record struct KeyHash(ulong A, ulong B, ulong C, ulong D)
{
    /// <summary>The first sixteen bytes: what the index holds. A hit is checked against the whole hash the commit records.</summary>
    internal KeyPrefix Prefix => new(A, B);

    internal static KeyHash Of(string agent, string key)
    {
        byte[] input = Encoding.UTF8.GetBytes(agent + "\0" + key);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return Read(hash);
    }

    internal static bool TryParse(string? hex, out KeyHash hash)
    {
        hash = default;
        Span<byte> bytes = stackalloc byte[32];

        if (hex is not { Length: 64 } || Convert.FromHexString(hex, bytes, out _, out int written) != System.Buffers.OperationStatus.Done || written != 32)
        {
            return false;
        }

        hash = Read(bytes);
        return true;
    }

    internal string ToHex()
    {
        Span<byte> bytes = stackalloc byte[32];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, A);
        BinaryPrimitives.WriteUInt64BigEndian(bytes[8..], B);
        BinaryPrimitives.WriteUInt64BigEndian(bytes[16..], C);
        BinaryPrimitives.WriteUInt64BigEndian(bytes[24..], D);
        return Convert.ToHexStringLower(bytes);
    }

    private static KeyHash Read(ReadOnlySpan<byte> bytes) =>
        new(BinaryPrimitives.ReadUInt64BigEndian(bytes), BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]), BinaryPrimitives.ReadUInt64BigEndian(bytes[16..]), BinaryPrimitives.ReadUInt64BigEndian(bytes[24..]));
}

/// <summary>The first sixteen bytes of a <see cref="KeyHash"/>, as the index keeps it.</summary>
internal readonly record struct KeyPrefix(ulong A, ulong B);

/// <summary>What a keyed write answers when it commits, and a retry of it is answered again: the status, <c>201</c> or <c>204</c>, and the <c>Location</c>.</summary>
internal sealed record KeyedAnswer(int Status, string? Location)
{
    /// <summary><c>204</c>, with no <c>Location</c>: what most writes answer.</summary>
    internal static KeyedAnswer NoContent => new(204, null);
}

/// <summary>What a keyed commit recorded, read back from its attachment: the key's hash, the request's fingerprint, its answer, and its request id.</summary>
internal sealed record KeyedCommit(long Position, KeyHash Key, string Fingerprint, KeyedAnswer Answer, string? RequestId);

/// <summary>
/// The outcome of looking a key up and, when it is free, claiming it for one
/// request: the position of the commit that holds it, a request still in
/// flight with it, or the claim; and the head the index was read to.
/// </summary>
internal sealed record KeyLookup(long? Position, bool InFlight, KeyClaim? Claim, long ReadTo);

/// <summary>
/// A key one request holds while it commits. Settled once, with the commit
/// or with nothing; disposing an unsettled claim releases the key.
/// </summary>
internal sealed class KeyClaim : IDisposable
{
    private readonly KeyIndex _index;
    private int _settled;

    internal KeyClaim(KeyIndex index, KeyHash key, string fingerprint)
    {
        _index = index;
        Key = key;
        Fingerprint = fingerprint;
    }

    internal KeyHash Key { get; }

    internal string Fingerprint { get; }

    /// <summary>What the commit answers, recorded with it.</summary>
    internal KeyedAnswer Answer { get; set; } = KeyedAnswer.NoContent;

    /// <summary>The commit landed at <paramref name="position"/>: the key is kept with it.</summary>
    internal void Committed(Position position)
    {
        if (Interlocked.Exchange(ref _settled, 1) == 0)
        {
            _index.Settle(this, position.Value);
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
/// The keys one open dataset's log records over the window, by the first
/// sixteen bytes of their hash, each with its commit's position; and the
/// keys requests on this instance hold in flight. Catching up reads the
/// commits after the last it read; a request holds its key from before it
/// is evaluated until it is answered, so two requests with one key never
/// reach the sequencer together on one writer.
/// </summary>
internal sealed class KeyIndex(long window, Func<Task> gate)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<KeyPrefix, long> _committed = [];
    private readonly HashSet<KeyPrefix> _inFlight = [];
    // Headers read at once while catching up.
    private const int Readers = 4;
    private long _readTo;
    private long _sweptAt;
    private Task? _warming;

    internal long Window => window;

    internal int Count
    {
        get
        {
            lock (_gate)
            {
                return _committed.Count;
            }
        }
    }

    /// <summary>
    /// Reads the commits of <paramref name="dataset"/> after the last read,
    /// within the window, and adds the keys they record. Every keyed commit
    /// at or below the head it answers is then known, whoever wrote it.
    /// </summary>
    internal async ValueTask<long> CatchUpAsync(Dataset dataset, CancellationToken cancellationToken)
    {
        // A warm-up in progress has the window to read: wait for it, then
        // read what it did not reach. One that failed leaves the reading to
        // this request.
        if (Volatile.Read(ref _warming) is { IsCompleted: false } warming)
        {
            try
            {
                await warming.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failed) when (failed is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // The request reads the log itself.
            }
        }

        return await CatchUpCoreAsync(dataset, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The warm-up: the window read on the thread pool, once per index.</summary>
    internal Task WarmAsync(Dataset dataset)
    {
        lock (_gate)
        {
            return _warming ??= Task.Run(async () =>
            {
                await gate().ConfigureAwait(false);
                await CatchUpCoreAsync(dataset, CancellationToken.None).ConfigureAwait(false);
            });
        }
    }

    private async ValueTask<long> CatchUpCoreAsync(Dataset dataset, CancellationToken cancellationToken)
    {
        // Two requests may read the same commits at once: adding a key is
        // idempotent, and the position read to only rises.
        long head = dataset.Head.Value;
        long from = Math.Max(Volatile.Read(ref _readTo), head - window);

        if (from < head)
        {
            // Headers alone, the delta never read (#310), a few at a time:
            // each is its own small read, and their order does not matter,
            // since a key is in at most one commit of the window.
            ParallelOptions parallel = new() { MaxDegreeOfParallelism = Readers, CancellationToken = cancellationToken };
            await Parallel.ForAsync(from + 1, head + 1, parallel, async (p, token) =>
            {
                CommitHeaderTerms header = await dataset.ReadMetadataAsync(new Position(p), token).ConfigureAwait(false);

                if (KeyAttachment.Read(header) is { } recorded)
                {
                    lock (_gate)
                    {
                        Add(recorded.Key.Prefix, recorded.Position);
                    }
                }
            }).ConfigureAwait(false);

            long seen = Volatile.Read(ref _readTo);

            while (head > seen && Interlocked.CompareExchange(ref _readTo, head, seen) is long found && found != seen)
            {
                seen = found;
            }
        }

        Evict(head);
        return head;
    }

    /// <summary>
    /// The position of the commit that may hold <paramref name="key"/>; or,
    /// when none does and no request holds it, a claim on it for this request.
    /// </summary>
    internal KeyLookup Lookup(KeyHash key, string fingerprint, long readTo)
    {
        KeyPrefix prefix = key.Prefix;

        lock (_gate)
        {
            if (_inFlight.Contains(prefix))
            {
                return new KeyLookup(null, InFlight: true, null, readTo);
            }

            if (_committed.TryGetValue(prefix, out long position) && position > _floor)
            {
                return new KeyLookup(position, InFlight: false, null, readTo);
            }

            _committed.Remove(prefix);
            _inFlight.Add(prefix);
            return new KeyLookup(null, InFlight: false, new KeyClaim(this, key, fingerprint), readTo);
        }
    }

    internal void Settle(KeyClaim claim, long? position)
    {
        lock (_gate)
        {
            _inFlight.Remove(claim.Key.Prefix);

            if (position is long at)
            {
                Add(claim.Key.Prefix, at);
            }
        }
    }

    // A key is recorded once, at the first position that carries it: the
    // writer never lets a second commit carry it while the first is in the
    // window.
    private void Add(KeyPrefix key, long position) => _committed.TryAdd(key, position);

    // Keys whose commits fell out of the window are forgotten (ADR 0246): a
    // lookup ignores them at once, and a sweep removes them once a quarter of
    // the window has passed since the last, so each sweep's cost is spread
    // over the commits that made it due.
    private void Evict(long head)
    {
        lock (_gate)
        {
            _floor = Math.Max(_floor, head - window);

            if (head - _sweptAt < Math.Max(1, window / 4))
            {
                return;
            }

            _sweptAt = head;
            List<KeyPrefix>? gone = null;

            foreach ((KeyPrefix key, long position) in _committed)
            {
                if (position <= _floor)
                {
                    (gone ??= []).Add(key);
                }
            }

            foreach (KeyPrefix key in gone ?? [])
            {
                _committed.Remove(key);
            }
        }
    }

    // Positions at or below it are out of the window.
    private long _floor;
}

/// <summary>
/// The host attachment a keyed commit carries (ADR 0246): the literal
/// <c>"{…}"^^varve:idempotencyKey</c>, its lexical form the canonical JSON
/// (RFC 8785) of the hash of the agent and the key, the request's
/// fingerprint, the status the write answered and its <c>Location</c>. The
/// key itself never enters the log.
/// </summary>
internal static class KeyAttachment
{
    /// <summary>The attachment a claim's commit records.</summary>
    internal static RdfTerm Of(KeyClaim claim)
    {
        System.Buffers.ArrayBufferWriter<byte> json = new();
        Json.Canonical.CanonicalJsonWriter writer = new(json);
        writer.StartObject();
        writer.WriteName("fingerprint"u8);
        writer.WriteString(claim.Fingerprint);
        writer.WriteName("keyHash"u8);
        writer.WriteString(claim.Key.ToHex());

        if (claim.Answer.Location is { } location)
        {
            writer.WriteName("location"u8);
            writer.WriteString(location);
        }

        writer.WriteName("status"u8);
        writer.WriteNumber(claim.Answer.Status);
        writer.EndObject();
        return RdfTerm.Literal(json.WrittenSpan.ToArray(), RdfTerm.Iri("https://w3id.org/varve/ns#idempotencyKey"u8));
    }

    /// <summary>The key a commit's header records, with what it answered, when it records one.</summary>
    internal static KeyedCommit? Read(CommitHeaderTerms header)
    {
        foreach (RdfTerm term in header.Attachments)
        {
            if (term.Kind != RdfTermKind.Literal
                || !term.DatatypeIri.SequenceEqual("https://w3id.org/varve/ns#idempotencyKey"u8)
                || !TryParse(term.Lexical, out KeyHash key, out string? fingerprint, out int status, out string? location))
            {
                continue;
            }

            string? cause = header.Cause is { } why ? Encoding.UTF8.GetString(why.Lexical) : null;
            return new KeyedCommit(header.Position.Value, key, fingerprint, new KeyedAnswer(status, location), cause);
        }

        return null;
    }

    /// <summary>The keyed commit at <paramref name="position"/>, read back from its header, when it records a key.</summary>
    internal static async ValueTask<KeyedCommit?> ReadAtAsync(Dataset dataset, long position, CancellationToken cancellationToken) =>
        position >= 1 && position <= dataset.Head.Value ? Read(await dataset.ReadMetadataAsync(new Position(position), cancellationToken).ConfigureAwait(false)) : null;

    private static bool TryParse(ReadOnlySpan<byte> lexical, out KeyHash key, out string fingerprint, out int status, out string? location)
    {
        key = default;
        string? hash = null;
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
                    case "keyHash":
                        hash = reader.GetString();
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

            return KeyHash.TryParse(hash, out key) && fingerprint.Length > 0;
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
