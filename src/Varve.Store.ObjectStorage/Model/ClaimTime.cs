// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Store.ObjectStorage.Model;

/// <summary>
/// When a claim was made, by the claimant's clock: for an operator to read,
/// never for the store to decide by (ADR 0136).
/// </summary>
/// <param name="Value">The instant.</param>
public readonly record struct ClaimTime(DateTimeOffset Value);
