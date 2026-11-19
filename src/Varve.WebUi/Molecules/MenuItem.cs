// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.WebUi.Molecules;

/// <summary>One action of a <c>Menu</c>.</summary>
/// <param name="Text">What the action is.</param>
/// <param name="Note">A line under it, announced as a status: progress, while the action runs.</param>
public sealed record MenuItem(string Text, string? Note = null);
