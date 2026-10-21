// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>
/// The largest blob a derived store can publish in one request: an object
/// store's single-request upload limit (ADR 0158). A merge whose output could
/// pass it is refused before it writes anything.
/// </summary>
[Contract(typeof(BackgroundExtentMerges.MergeRefusedPastTheSinglePutLimit), Role = "a derived store whose blobs are bounded by a single request")]
public interface IDerivedStoreLimits
{
    /// <summary>The bytes one blob may hold.</summary>
    ByteCount MaxBlobBytes { get; }
}
