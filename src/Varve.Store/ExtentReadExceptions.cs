// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Store;

/// <summary>
/// This instance's reader record was forgotten while it still read (ADR 0160):
/// an extent its catalog names may be gone, and the read that found one is
/// failed rather than answered. Never a wrong result: the instance attaches
/// again, with a fresh record and the newest catalog.
/// </summary>
/// <remarks>Over HTTP, <c>503 reader-forgotten</c>.</remarks>
[DesignDecision(typeof(ExtentDeletionByReaderAnnouncements.AForgottenReaderFailsAndReattaches), Scope = ExceptionScope.Boundary)]
public sealed class ReaderForgottenException : IOException
{
    /// <summary>The record was forgotten.</summary>
    public ReaderForgottenException()
    {
    }

    /// <summary>The record was forgotten, and how.</summary>
    public ReaderForgottenException(string message)
        : base(message)
    {
    }

    /// <summary>The record was forgotten, how, and the cause.</summary>
    public ReaderForgottenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A read opened explicitly unannounced found an extent its catalog names gone
/// (ADR 0160): without a record, the writer may delete what it reads. The read
/// fails; it is never answered wrong, and this is not damage.
/// </summary>
/// <remarks>Over HTTP and on the command line, <c>read-unannounced-extent-gone</c>.</remarks>
[DesignDecision(typeof(ExtentDeletionByReaderAnnouncements.AnUnannouncedReadFailsOnAGoneExtent), Scope = ExceptionScope.Boundary)]
public sealed class UnannouncedExtentGoneException : IOException
{
    /// <summary>An extent was gone.</summary>
    public UnannouncedExtentGoneException()
    {
    }

    /// <summary>An extent was gone, and which.</summary>
    public UnannouncedExtentGoneException(string message)
        : base(message)
    {
    }

    /// <summary>An extent was gone, which, and the cause.</summary>
    public UnannouncedExtentGoneException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A derived blob's name was taken in the store with other bytes (ADR 0158):
/// a superseded writer wrote it. Names are never reused, so the store writes
/// the set again under the next sequence.
/// </summary>
[DesignDecision(typeof(BackgroundExtentMerges.ATakenNameTakesTheNextSequence), Scope = ExceptionScope.Boundary)]
public sealed class DerivedNameTakenException : IOException
{
    /// <summary>A name was taken.</summary>
    public DerivedNameTakenException()
    {
    }

    /// <summary>A name was taken, and which.</summary>
    public DerivedNameTakenException(string message)
        : base(message)
    {
    }

    /// <summary>A name was taken, which, and the cause.</summary>
    public DerivedNameTakenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// An object store refused a request for the workload's identity: <c>401</c>
/// or <c>403</c>. The message names the role the identity needs (ADR 0137,
/// ADR 0160).
/// </summary>
public sealed class ObjectStoreAccessDeniedException : IOException
{
    /// <summary>Access was denied.</summary>
    public ObjectStoreAccessDeniedException()
    {
    }

    /// <summary>Access was denied, and what for.</summary>
    public ObjectStoreAccessDeniedException(string message)
        : base(message)
    {
    }

    /// <summary>Access was denied, what for, and the cause.</summary>
    public ObjectStoreAccessDeniedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
