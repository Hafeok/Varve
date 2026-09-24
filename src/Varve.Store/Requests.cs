// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Varve.Rdf;

namespace Varve.Store;

/// <summary>
/// A term in a commit request: an <see cref="RdfTerm"/>, or a handle naming a
/// term this dataset already has.
/// </summary>
/// <remarks>
/// <para>
/// A request is over terms, not ids — the caller does not know ids yet (spec
/// T1 step 2). An <see cref="RdfTerm"/> converts implicitly.
/// </para>
/// <para>
/// **A blank node given as an <see cref="RdfTerm"/> is always a fresh node.**
/// Labels are scoped to the request: each distinct label is one new node, and
/// the same label in a later request is another — even a label that
/// <c>TryExternalise</c> produced. An existing blank node is addressed by its
/// store identity, <see cref="Existing"/> (ADR 0044, Q1's in-process half).
/// </para>
/// <para>
/// <c>default(RequestTerm)</c> is <see cref="None"/>: the default graph in the
/// graph position, absent metadata elsewhere, and refused as a subject,
/// predicate or object.
/// </para>
/// </remarks>
public readonly struct RequestTerm : IEquatable<RequestTerm>
{
    private RequestTerm(RdfTerm? term, TermHandle handle)
    {
        Term = term;
        Handle = handle;
    }

    /// <summary>No term.</summary>
    public static RequestTerm None => default;

    /// <summary>The term, when this is not a handle.</summary>
    public RdfTerm? Term { get; }

    /// <summary>The handle, when <see cref="IsExisting"/>.</summary>
    public TermHandle Handle { get; }

    /// <summary>True when this names a term the dataset already has, by handle.</summary>
    public bool IsExisting => !Handle.IsNone;

    /// <summary>True for <see cref="None"/>.</summary>
    public bool IsNone => Term is null && Handle.IsNone;

    /// <summary>
    /// A term this dataset already has, by the handle any read of it returned.
    /// This is how an existing blank node is addressed. A handle the dataset
    /// never issued fails the request with an exception and leaves no trace.
    /// </summary>
    public static RequestTerm Existing(TermHandle handle)
    {
        if (handle.IsNone)
        {
            throw new ArgumentException("TermHandle.None names no term.", nameof(handle));
        }

        return new RequestTerm(null, handle);
    }

    /// <summary>A term given by value.</summary>
    public static RequestTerm FromTerm(RdfTerm term)
    {
        ArgumentNullException.ThrowIfNull(term);
        return new RequestTerm(term, TermHandle.None);
    }

    /// <summary>A term given by value.</summary>
    public static implicit operator RequestTerm(RdfTerm term) => FromTerm(term);

    /// <inheritdoc />
    public bool Equals(RequestTerm other) => Equals(Term, other.Term) && Handle == other.Handle;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RequestTerm other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Term, Handle);

    /// <summary>Compares the term and the handle.</summary>
    public static bool operator ==(RequestTerm left, RequestTerm right) => left.Equals(right);

    /// <summary>Compares the term and the handle.</summary>
    public static bool operator !=(RequestTerm left, RequestTerm right) => !left.Equals(right);
}

/// <summary>
/// What a commit records about itself besides its delta (spec §1, <c>meta</c>).
/// The timestamp is not here: the sequencer assigns it, and never accepts one.
/// </summary>
public sealed class CommitMetadata
{
    /// <summary>Who made the change. A term, never an inline string, so that it can later be private.</summary>
    public RequestTerm Agent { get; init; }

    /// <summary>Why.</summary>
    public RequestTerm Cause { get; init; }

    /// <summary>
    /// The named graph the commit declares it is about. **A declaration recorded
    /// in metadata, never enforced by the store**; a validator may enforce it
    /// (specification 1.2, §1).
    /// </summary>
    public RequestTerm GraphScope { get; init; }
}

/// <summary>
/// A request to change the dataset: an ordered list of assertions and
/// retractions over terms, metadata, an optional expected position, and the
/// validators to run (spec T1).
/// </summary>
/// <remarks>
/// The operations are applied in order to an overlay on the head, and the
/// log records the net result: asserting a present quad, retracting an absent
/// one, and asserting then retracting an absent one contribute nothing
/// (ADR 0010).
/// </remarks>
public sealed class CommitRequest
{
    private readonly List<Operation> _operations = [];

    /// <summary>
    /// The position the caller read at. When given and not the readable head,
    /// the request is refused with <see cref="CommitOutcome.Conflict"/> and
    /// changes nothing (ADR 0011).
    /// </summary>
    public long? ExpectedPosition { get; init; }

    /// <summary>What the commit records about itself.</summary>
    public CommitMetadata Metadata { get; init; } = new();

    /// <summary>Validators run against the proposed state, in order. The first reject wins.</summary>
    public IList<ICommitValidator> Validators { get; } = [];

    /// <summary>How many operations the request holds.</summary>
    public int Count => _operations.Count;

    /// <summary>Asserts a quad in the default graph.</summary>
    public CommitRequest Assert(RequestTerm subject, RequestTerm predicate, RequestTerm @object) =>
        Add(true, subject, predicate, @object, RequestTerm.None);

    /// <summary>Asserts a quad in a named graph.</summary>
    public CommitRequest Assert(RequestTerm subject, RequestTerm predicate, RequestTerm @object, RequestTerm graph) =>
        Add(true, subject, predicate, @object, graph);

    /// <summary>Retracts a quad from the default graph.</summary>
    public CommitRequest Retract(RequestTerm subject, RequestTerm predicate, RequestTerm @object) =>
        Add(false, subject, predicate, @object, RequestTerm.None);

    /// <summary>Retracts a quad from a named graph.</summary>
    public CommitRequest Retract(RequestTerm subject, RequestTerm predicate, RequestTerm @object, RequestTerm graph) =>
        Add(false, subject, predicate, @object, graph);

    internal ReadOnlySpan<Operation> Operations => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_operations);

    private CommitRequest Add(bool assert, RequestTerm subject, RequestTerm predicate, RequestTerm @object, RequestTerm graph)
    {
        if (subject.IsNone || predicate.IsNone || @object.IsNone)
        {
            throw new ArgumentException("A quad has a subject, a predicate and an object; only the graph may be absent.");
        }

        _operations.Add(new Operation(assert, subject, predicate, @object, graph));
        return this;
    }

    internal readonly struct Operation
    {
        internal Operation(bool assert, RequestTerm subject, RequestTerm predicate, RequestTerm @object, RequestTerm graph)
        {
            IsAssert = assert;
            Subject = subject;
            Predicate = predicate;
            Object = @object;
            Graph = graph;
        }

        internal bool IsAssert { get; }

        internal RequestTerm Subject { get; }

        internal RequestTerm Predicate { get; }

        internal RequestTerm Object { get; }

        internal RequestTerm Graph { get; }
    }
}

/// <summary>How a commit request ended (spec T1).</summary>
public enum CommitOutcome
{
    /// <summary>The change is in the log at <see cref="CommitResult.Position"/>.</summary>
    Committed,

    /// <summary>The request changed nothing. No commit, no trace; the position is the head.</summary>
    NoChange,

    /// <summary>The expected position was not the head, which the position carries. Nothing changed.</summary>
    Conflict,

    /// <summary>A validator refused. No commit, no trace; <see cref="CommitResult.Report"/> says why.</summary>
    Rejected,

    /// <summary>
    /// The dataset cannot take a commit now: it is in the failed state, or its
    /// default projection is not at the head. <see cref="CommitResult.Reason"/> says which.
    /// </summary>
    Unavailable,
}

/// <summary>The answer to a commit request. A <see cref="CommitOutcome.Conflict"/> is a normal answer, not an error.</summary>
public readonly struct CommitResult : IEquatable<CommitResult>
{
    private CommitResult(CommitOutcome outcome, long position, IReadOnlyList<RdfTerm>? report, string? reason)
    {
        Outcome = outcome;
        Position = position;
        Report = report ?? [];
        Reason = reason;
    }

    /// <summary>How it ended.</summary>
    public CommitOutcome Outcome { get; }

    /// <summary>
    /// The new position when committed; otherwise the readable head at the
    /// time of the answer.
    /// </summary>
    public long Position { get; }

    /// <summary>The validator's report, when rejected; empty otherwise.</summary>
    public IReadOnlyList<RdfTerm> Report { get; }

    /// <summary>Why, when unavailable.</summary>
    public string? Reason { get; }

    internal static CommitResult Committed(long position) => new(CommitOutcome.Committed, position, null, null);

    internal static CommitResult NoChange(long head) => new(CommitOutcome.NoChange, head, null, null);

    internal static CommitResult Conflict(long head) => new(CommitOutcome.Conflict, head, null, null);

    internal static CommitResult Rejected(long head, IReadOnlyList<RdfTerm> report) => new(CommitOutcome.Rejected, head, report, null);

    internal static CommitResult Unavailable(long head, string reason) => new(CommitOutcome.Unavailable, head, null, reason);

    /// <inheritdoc />
    public bool Equals(CommitResult other) =>
        Outcome == other.Outcome && Position == other.Position && ReferenceEquals(Report, other.Report) && Reason == other.Reason;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CommitResult other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Outcome, Position);

    /// <summary>Compares every field; reports by reference.</summary>
    public static bool operator ==(CommitResult left, CommitResult right) => left.Equals(right);

    /// <summary>Compares every field; reports by reference.</summary>
    public static bool operator !=(CommitResult left, CommitResult right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => Outcome + "(" + Position + ")";
}

/// <summary>
/// A pre-commit validator (ADR 0017): it sees what the dataset would look like
/// and what is changing, and nothing else, and decides.
/// </summary>
/// <remarks>
/// SPARQL-free and SHACL-free by ADR 0005. A validator driven by either is a
/// layer 5 integration that implements this.
/// </remarks>
public interface ICommitValidator
{
    /// <summary>
    /// Decides whether a commit may land. <paramref name="proposed"/> is
    /// <c>Overlay(G_head, δ)</c>; <paramref name="delta"/> is <c>δ</c>, in
    /// <paramref name="proposed"/>'s handles.
    /// </summary>
    ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta);
}

/// <summary>A validator's decision: accept, optionally with an attachment, or reject with a report.</summary>
public readonly struct ValidationVerdict : IEquatable<ValidationVerdict>
{
    private ValidationVerdict(bool accepted, RdfTerm? attachment, IReadOnlyList<RdfTerm>? report)
    {
        IsAccepted = accepted;
        Attachment = attachment;
        Report = report ?? [];
    }

    /// <summary>Whether the commit may land.</summary>
    public bool IsAccepted { get; }

    /// <summary>A term recorded in the commit's metadata, for example a validation report's IRI.</summary>
    public RdfTerm? Attachment { get; }

    /// <summary>Why the commit was refused. Triple terms can carry a report graph.</summary>
    public IReadOnlyList<RdfTerm> Report { get; }

    /// <summary>Accept.</summary>
    public static ValidationVerdict Accept() => new(true, null, null);

    /// <summary>Accept, and record a term in the commit's metadata.</summary>
    public static ValidationVerdict Accept(RdfTerm attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        return new ValidationVerdict(true, attachment, null);
    }

    /// <summary>Refuse. Nothing reaches the log or the dictionary.</summary>
    public static ValidationVerdict Reject(IReadOnlyList<RdfTerm> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new ValidationVerdict(false, null, report);
    }

    /// <inheritdoc />
    public bool Equals(ValidationVerdict other) =>
        IsAccepted == other.IsAccepted && Equals(Attachment, other.Attachment) && ReferenceEquals(Report, other.Report);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ValidationVerdict other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(IsAccepted, Attachment);

    /// <summary>Compares every field; reports by reference.</summary>
    public static bool operator ==(ValidationVerdict left, ValidationVerdict right) => left.Equals(right);

    /// <summary>Compares every field; reports by reference.</summary>
    public static bool operator !=(ValidationVerdict left, ValidationVerdict right) => !left.Equals(right);
}
