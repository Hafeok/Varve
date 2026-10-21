// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Store.ObjectStorage.Model;

/// <summary>Whether an open writes a reader record (ADR 0160).</summary>
public enum ReaderAnnouncing
{
    /// <summary>
    /// It does, and an open whose record cannot be written fails, naming the
    /// role it needs. The default.
    /// </summary>
    Required,

    /// <summary>
    /// It writes none, for an identity that may only read: asked for
    /// explicitly. The writer may delete what such a read holds, and a read
    /// that finds an extent gone fails with <see cref="UnannouncedExtentGoneException"/>,
    /// never with a wrong result.
    /// </summary>
    Unannounced,
}
