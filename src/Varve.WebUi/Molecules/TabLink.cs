// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.WebUi.Molecules;

/// <summary>One view of a screen in <see cref="Tabs"/>.</summary>
/// <param name="Text">The view's word: "Table", "Source".</param>
/// <param name="Href">Its address, relative to the base.</param>
/// <param name="Current">Whether it is the view shown, marked <c>aria-current="page"</c>.</param>
public sealed record TabLink(string Text, string Href, bool Current);
