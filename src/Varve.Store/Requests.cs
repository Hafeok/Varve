// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Varve.Rdf;
using Varve.Store.Log;

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
