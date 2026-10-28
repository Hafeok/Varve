// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading;
using System.Threading.Tasks;
using Varve.Delta.Model;
using Varve.Protocol.Client;

namespace Varve.WebUi;

/// <summary>
/// A live tail's checkpoint for one screen's life (ADR 0129): the tail resumes
/// from it after a dropped connection, and nothing outlives the screen.
/// </summary>
internal sealed class TabCheckpoint : IFollowCheckpoint
{
    private FeedPosition? _position;

    public ValueTask<FeedPosition?> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(_position);

    public ValueTask SaveAsync(FeedPosition position, CancellationToken cancellationToken)
    {
        _position = position;
        return ValueTask.CompletedTask;
    }
}
