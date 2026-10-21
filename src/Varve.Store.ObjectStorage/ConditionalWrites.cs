// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Varve.Store.Log;

namespace Varve.Store.ObjectStorage;

/// <summary>What a create found.</summary>
internal enum CreateOutcome
{
    /// <summary>The object is ours: this attempt wrote it, or an earlier one whose answer was lost.</summary>
    Ours,

    /// <summary>Another writer's object holds the name.</summary>
    Taken,
}

/// <summary>
/// The backend's writes, retried and resolved: a write whose outcome is
/// unknown is retried, and a create that finds an object reads it back and
/// compares (ADR 0135, <c>LostResponseReadsBack</c>).
/// </summary>
internal static class ConditionalWrites
{
    /// <summary>How many times a write is tried before its outcome is reported unknown.</summary>
    private const int Attempts = 6;

    /// <summary>Everything an object holds, or null when there is none.</summary>
    public static async ValueTask<ObjectRead?> ReadWholeAsync(IObjectStore store, ObjectName name, CancellationToken cancellationToken)
    {
        ObjectRead? first = await store.ReadAsync(name, new ByteOffset(0), new ByteCount(long.MaxValue), cancellationToken).ConfigureAwait(false);

        if (first is null || first.Bytes.Length == first.Length.Value)
        {
            return first;
        }

        // A store that pages its reads: the rest, from the same version.
        ArrayBufferWriter<byte> bytes = new(checked((int)first.Length.Value));
        bytes.Write(first.Bytes.Span);

        while (bytes.WrittenCount < first.Length.Value)
        {
            ObjectRead? next = await store.ReadAsync(name, new ByteOffset(bytes.WrittenCount), new ByteCount(first.Length.Value - bytes.WrittenCount), cancellationToken).ConfigureAwait(false);

            if (next is null || next.Version != first.Version || next.Bytes.IsEmpty)
            {
                throw new IOException("The object '" + name + "' changed while it was read; an object this backend reads in parts is never replaced.");
            }

            bytes.Write(next.Bytes.Span);
        }

        return first with { Bytes = bytes.WrittenMemory };
    }

    /// <summary>
    /// Creates <paramref name="name"/> holding <paramref name="content"/>, or
    /// finds whose it is. Throws only when no attempt could tell.
    /// </summary>
    /// <exception cref="IOException">Every attempt failed without an answer: the outcome is unknown.</exception>
    public static async ValueTask<(CreateOutcome Outcome, ReadOnlyMemory<byte> Existing)> CreateAsync(
        IObjectStore store, ObjectName name, ReadOnlyMemory<byte> content, TimeProvider clock, CancellationToken cancellationToken)
    {
        Exception? last = null;

        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(Backoff(attempt), clock, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                if (await store.CreateIfAbsentAsync(name, content, cancellationToken).ConfigureAwait(false) is not null)
                {
                    return (CreateOutcome.Ours, ReadOnlyMemory<byte>.Empty);
                }

                ObjectRead? existing = await ReadWholeAsync(store, name, cancellationToken).ConfigureAwait(false);

                if (existing is null)
                {
                    // Deleted between the create and the read: try again.
                    continue;
                }

                // A chunk's bytes carry the writer's epoch, so equal bytes are
                // this writer's own earlier attempt, whose answer was lost.
                return existing.Bytes.Span.SequenceEqual(content.Span)
                    ? (CreateOutcome.Ours, ReadOnlyMemory<byte>.Empty)
                    : (CreateOutcome.Taken, existing.Bytes);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error) when (error is not WriterSupersededException and not ObjectStoreAccessDeniedException)
            {
                last = error;
            }
        }

        throw new IOException("Creating '" + name + "' failed " + Attempts + " times without an answer; whether it exists is unknown until the dataset is opened again.", last);
    }

    /// <summary>
    /// Creates <paramref name="name"/> streamed from a staged file (ADR 0158),
    /// or finds whose it is: equal bytes, read back in ranges, are this
    /// writer's earlier attempt.
    /// </summary>
    /// <exception cref="IOException">Every attempt failed without an answer: the outcome is unknown.</exception>
    public static async ValueTask<CreateOutcome> CreateAsync(IObjectStore store, ObjectName name, ObjectContent content, TimeProvider clock, CancellationToken cancellationToken)
    {
        Exception? last = null;

        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(Backoff(attempt), clock, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                if (await store.CreateIfAbsentAsync(name, content, cancellationToken).ConfigureAwait(false) is not null)
                {
                    return CreateOutcome.Ours;
                }

                bool? same = await SameAsync(store, name, content, cancellationToken).ConfigureAwait(false);

                if (same is { } found)
                {
                    return found ? CreateOutcome.Ours : CreateOutcome.Taken;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error) when (error is not WriterSupersededException and not ObjectStoreAccessDeniedException)
            {
                last = error;
            }
        }

        throw new IOException("Creating '" + name + "' failed " + Attempts + " times without an answer; whether it exists is unknown until the dataset is opened again.", last);
    }

    // Whether the object holds the staged file's bytes; null when it is gone.
    private static async ValueTask<bool?> SameAsync(IObjectStore store, ObjectName name, ObjectContent content, CancellationToken cancellationToken)
    {
        const int range = 1 << 20;
        byte[] mine = new byte[range];
        using Stream file = content.OpenRead();
        long offset = 0;
        ObjectVersion? version = null;

        while (true)
        {
            ObjectRead? read = await store.ReadAsync(name, new ByteOffset(offset), new ByteCount(range), cancellationToken).ConfigureAwait(false);

            if (read is null)
            {
                return offset == 0 ? null : throw new IOException("The object '" + name + "' was deleted while it was read back.");
            }

            if (read.Length.Value != content.Length.Value)
            {
                return false;
            }

            if (version is { } held && read.Version != held)
            {
                throw new IOException("The object '" + name + "' changed while it was read back; an object this backend creates is never replaced.");
            }

            version = read.Version;

            if (read.Bytes.IsEmpty)
            {
                return offset == content.Length.Value;
            }

            int count = read.Bytes.Length;
            await file.ReadExactlyAsync(mine.AsMemory(0, count), cancellationToken).ConfigureAwait(false);

            if (!read.Bytes.Span.SequenceEqual(mine.AsSpan(0, count)))
            {
                return false;
            }

            offset += count;

            if (offset == content.Length.Value)
            {
                return true;
            }
        }
    }

    /// <summary>
    /// Replaces <paramref name="name"/> if it is still <paramref name="expected"/>.
    /// Returns the new version, or null when another writer replaced it first.
    /// </summary>
    /// <exception cref="IOException">Every attempt failed without an answer.</exception>
    public static async ValueTask<ObjectVersion?> ReplaceAsync(
        IObjectStore store, ObjectName name, ObjectVersion expected, ReadOnlyMemory<byte> content, TimeProvider clock, CancellationToken cancellationToken)
    {
        Exception? last = null;

        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(Backoff(attempt), clock, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                ObjectVersion? replaced = await store.ReplaceIfUnchangedAsync(name, expected, content, cancellationToken).ConfigureAwait(false);

                if (replaced is not null)
                {
                    return replaced;
                }

                // Lost, or won by an earlier attempt whose answer was lost.
                ObjectRead? current = await ReadWholeAsync(store, name, cancellationToken).ConfigureAwait(false);
                return current is not null && last is not null && current.Bytes.Span.SequenceEqual(content.Span) ? current.Version : null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error) when (error is not ObjectStoreAccessDeniedException)
            {
                last = error;
            }
        }

        throw new IOException("Replacing '" + name + "' failed " + Attempts + " times without an answer.", last);
    }

    // 50 ms, doubling: how long to wait before trying again, never whether.
    private static TimeSpan Backoff(int attempt) => TimeSpan.FromMilliseconds(50 << Math.Min(attempt - 1, 6));
}
