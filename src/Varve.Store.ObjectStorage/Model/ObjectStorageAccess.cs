// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Store.ObjectStorage.Model;

/// <summary>How a dataset in an object store is opened.</summary>
public enum ObjectStorageAccess
{
    /// <summary>
    /// As the dataset's writer: the open claims the next writer epoch and
    /// fences the log with it (ADR 0136).
    /// </summary>
    Write,

    /// <summary>For reads only: the open claims nothing and changes nothing in the store.</summary>
    ReadOnly,
}
