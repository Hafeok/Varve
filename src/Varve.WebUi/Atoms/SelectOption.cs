// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.WebUi.Atoms;

/// <summary>One choice of a <c>Select</c>: the value it stands for and the words it shows.</summary>
/// <param name="Value">What the choice stands for.</param>
/// <param name="Text">What it shows.</param>
public sealed record SelectOption(string Value, string Text);
