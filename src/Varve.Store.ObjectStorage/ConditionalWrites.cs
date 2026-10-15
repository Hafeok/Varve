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
            catch (Exception error) when (error is not WriterSupersededException)
            {
                last = error;
            }
        }

        throw new IOException("Creating '" + name + "' failed " + Attempts + " times without an answer; whether it exists is unknown until the dataset is opened again.", last);
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
            catch (Exception error)
            {
                last = error;
            }
        }

        throw new IOException("Replacing '" + name + "' failed " + Attempts + " times without an answer.", last);
    }

    // 50 ms, doubling: how long to wait before trying again, never whether.
    private static TimeSpan Backoff(int attempt) => TimeSpan.FromMilliseconds(50 << Math.Min(attempt - 1, 6));
}
