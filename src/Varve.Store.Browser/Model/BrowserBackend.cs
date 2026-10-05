// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Store.Log;

namespace Varve.Store.Browser.Model;

/// <summary>Which browser storage a <see cref="BrowserStorage"/> keeps its dataset in.</summary>
public enum BrowserBackend
{
    /// <summary>
    /// The origin private file system, through synchronous access handles: a
    /// dedicated worker only. Declares <see cref="Durability.Committed"/>.
    /// </summary>
    OriginPrivateFileSystem,

    /// <summary>
    /// IndexedDB, where synchronous access handles do not exist: the page's own
    /// thread, or a browser without them. Declares <see cref="Durability.Committed"/>.
    /// </summary>
    IndexedDb,
}
