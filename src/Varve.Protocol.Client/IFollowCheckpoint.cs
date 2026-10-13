// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Delta.Model;

namespace Varve.Protocol.Client;

/// <summary>
/// Where a follower keeps the position it has handled (ADR 0129): a file, a
/// table, a quad in the consumer's own dataset. <see cref="SparqlHttpClient.FollowAsync"/>
/// saves a position only after its handler has returned for that commit, so
/// a consumer that saves it in the same transaction as the handler's effect
/// gets each commit's effect exactly once.
/// </summary>
[Contract(typeof(FollowingTheCommitsResource.CheckpointIsTheCallers), Role = "the caller's store of the last position a follower handled")]
public interface IFollowCheckpoint
{
    /// <summary>The last position saved, or null when none has been.</summary>
    ValueTask<FeedPosition?> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Records that every commit up to and including <paramref name="position"/> was handled.</summary>
    ValueTask SaveAsync(FeedPosition position, CancellationToken cancellationToken);
}

/// <summary>
/// A checkpoint in a file (ADR 0129): the position in decimal, replaced
/// atomically, a temporary file renamed over it, so a crash leaves the old
/// position or the new one and never half of either.
/// </summary>
public sealed class FileFollowCheckpoint : IFollowCheckpoint
{
    private readonly string _path;

    /// <summary>A checkpoint kept at <paramref name="path"/>.</summary>
    public FileFollowCheckpoint(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = Path.GetFullPath(path);
    }

    /// <inheritdoc />
    public async ValueTask<FeedPosition?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        string text = (await File.ReadAllTextAsync(_path, Encoding.ASCII, cancellationToken).ConfigureAwait(false)).Trim();
        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long position)
            ? new FeedPosition(position)
            : throw new InvalidDataException(_path + " is a follower's checkpoint and holds '" + text + "', not a position.");
    }

    /// <inheritdoc />
    public async ValueTask SaveAsync(FeedPosition position, CancellationToken cancellationToken)
    {
        string temporary = _path + ".tmp";
        await File.WriteAllTextAsync(temporary, position.ToString(), Encoding.ASCII, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, _path, overwrite: true);
    }
}
