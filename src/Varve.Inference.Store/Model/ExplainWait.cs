// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Inference.Store.Model;

/// <summary>How long explain waits for a pass to let go of the live closures (ADR 0195 point 6, amended 2026-11-25).</summary>
public readonly record struct ExplainWait(TimeSpan Value);

/// <summary>When a projection's pass began, by its clock.</summary>
public readonly record struct PassStart(DateTimeOffset Value);
