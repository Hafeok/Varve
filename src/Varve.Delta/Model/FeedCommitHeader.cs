// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Rdf;

namespace Varve.Delta.Model;

/// <summary>
/// What a commit record's header and metadata lines say (<c>change-feed.md</c>
/// §2.1), with its terms as handles a <see cref="Varve.Delta.TermNamer"/> names:
/// the shape a store hands the writer without the writer knowing the store.
/// </summary>
/// <param name="Position">The commit's position.</param>
/// <param name="Kind">Its kind.</param>
/// <param name="Timestamp">When it closed.</param>
/// <param name="Agent">Its agent, or <see cref="TermHandle.None"/>.</param>
/// <param name="Cause">Its cause, or <see cref="TermHandle.None"/>.</param>
/// <param name="Scope">Its declared named-graph scope, or <see cref="TermHandle.None"/>.</param>
/// <param name="Attachments">What validators attached to it.</param>
public readonly record struct FeedCommitHeader(
    FeedPosition Position,
    FeedCommitKind Kind,
    FeedTimestamp Timestamp,
    TermHandle Agent,
    TermHandle Cause,
    TermHandle Scope,
    ReadOnlyMemory<TermHandle> Attachments);
