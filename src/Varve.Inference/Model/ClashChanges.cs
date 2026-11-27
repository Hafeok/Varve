// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;

namespace Varve.Inference.Model;

/// <summary>
/// The clashes a closure kept and dropped since its owner last asked: a clash
/// is a new object each time it is kept, so one that came and went since is
/// in both (ADR 0195 point 6, amended 2026-11-25).
/// </summary>
/// <param name="Came">The clashes kept since.</param>
/// <param name="Went">The clashes dropped since.</param>
public sealed record ClashChanges(IReadOnlyList<Clash> Came, IReadOnlyList<Clash> Went);
