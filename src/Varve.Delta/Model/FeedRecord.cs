// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Varve.Rdf;

namespace Varve.Delta.Model;

/// <summary>Which record of the delta format this is (<c>change-feed.md</c> §2).</summary>
public enum FeedRecordKind : byte
{
    /// <summary>One commit: its header, metadata and changes.</summary>
    Commit = 0,

    /// <summary>A diff between two positions (R3).</summary>
    Diff = 1,

    /// <summary>The stream was cut; <see cref="FeedRecord.Problem"/> says why.</summary>
    Error = 2,
}

/// <summary>A commit's kind, as its header names it (T1, <c>change-feed.md</c> §2.1).</summary>
public enum FeedCommitKind : byte
{
    /// <summary><c>Data</c>: quads asserted and retracted.</summary>
    Data = 0,

    /// <summary><c>Settings</c>: the dataset's settings changed; no change lines (I4).</summary>
    Settings = 1,

    /// <summary><c>Erasure</c>: a data subject's key destroyed; no change lines (I4).</summary>
    Erasure = 2,
}

/// <summary>Whether a change asserts or retracts its quad.</summary>
public enum FeedChangeKind : byte
{
    /// <summary><c>+</c>: the quad is in the later state and not the earlier.</summary>
    Assert = 0,

    /// <summary><c>-</c>: the quad is in the earlier state and not the later.</summary>
    Retract = 1,
}

/// <summary>One change line: a quad and its sign. A quad in the default graph has no <see cref="Graph"/>.</summary>
public readonly record struct FeedChange(FeedChangeKind Kind, RdfTerm Subject, RdfTerm Predicate, RdfTerm Object, RdfTerm? Graph);

/// <summary>
/// One record of <c>application/vnd.varve.delta; version=1</c> as
/// <see cref="Varve.Delta.ChangeFeedReader"/> reads it (<c>change-feed.md</c> §2).
/// </summary>
/// <remarks>
/// A commit record has a <see cref="Position"/>, a <see cref="CommitKind"/>, a
/// <see cref="Timestamp"/> and its metadata. A diff record has
/// <see cref="From"/> and <see cref="Position"/> (its <c>to</c>). An error
/// record has only its <see cref="Problem"/>. Blank nodes keep the labels the
/// server gave them, which name the same node in every record of one dataset
/// (ADR 0098).
/// </remarks>
public sealed class FeedRecord
{
    private FeedRecord(
        FeedRecordKind kind,
        FeedPosition position,
        FeedPosition from,
        FeedCommitKind commitKind,
        FeedTimestamp timestamp,
        RdfTerm? agent,
        RdfTerm? cause,
        RdfTerm? scope,
        IReadOnlyList<RdfTerm> attachments,
        IReadOnlyList<FeedChange> changes,
        FeedProblemType problem)
    {
        Kind = kind;
        Position = position;
        From = from;
        CommitKind = commitKind;
        Timestamp = timestamp;
        Agent = agent;
        Cause = cause;
        Scope = scope;
        Attachments = attachments;
        Changes = changes;
        Problem = problem;
    }

    /// <summary>Which record this is.</summary>
    public FeedRecordKind Kind { get; }

    /// <summary>A commit's position, or a diff's <c>to</c>.</summary>
    public FeedPosition Position { get; }

    /// <summary>A diff's <c>from</c>.</summary>
    public FeedPosition From { get; }

    /// <summary>A commit's kind.</summary>
    public FeedCommitKind CommitKind { get; }

    /// <summary>A commit's timestamp, in UTC.</summary>
    public FeedTimestamp Timestamp { get; }

    /// <summary>A commit's agent, if it has one.</summary>
    public RdfTerm? Agent { get; }

    /// <summary>A commit's cause, if it has one.</summary>
    public RdfTerm? Cause { get; }

    /// <summary>A commit's declared named-graph scope, if it has one.</summary>
    public RdfTerm? Scope { get; }

    /// <summary>What validators attached to a commit.</summary>
    public IReadOnlyList<RdfTerm> Attachments { get; }

    /// <summary>The changes, assertions first.</summary>
    public IReadOnlyList<FeedChange> Changes { get; }

    /// <summary>An error record's problem type; the default for any other record.</summary>
    public FeedProblemType Problem { get; }

    /// <summary>A commit record.</summary>
    public static FeedRecord ForCommit(
        FeedPosition position,
        FeedCommitKind kind,
        FeedTimestamp timestamp,
        RdfTerm? agent,
        RdfTerm? cause,
        RdfTerm? scope,
        IReadOnlyList<RdfTerm> attachments,
        IReadOnlyList<FeedChange> changes)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        ArgumentNullException.ThrowIfNull(changes);
        return new(FeedRecordKind.Commit, position, default, kind, timestamp, agent, cause, scope, attachments, changes, default);
    }

    /// <summary>A diff record.</summary>
    public static FeedRecord ForDiff(FeedPosition from, FeedPosition to, IReadOnlyList<FeedChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return new(FeedRecordKind.Diff, to, from, default, default, null, null, null, [], changes, default);
    }

    /// <summary>An error record.</summary>
    public static FeedRecord ForError(FeedProblemType problem) =>
        new(FeedRecordKind.Error, default, default, default, default, null, null, null, [], [], problem);
}
