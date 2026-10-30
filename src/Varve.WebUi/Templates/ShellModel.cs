// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;

namespace Varve.WebUi.Templates;

/// <summary>A group of the navigation rail: Operate, History, Declarations.</summary>
/// <param name="Name">The group's label.</param>
/// <param name="Entries">Its screens.</param>
public sealed record NavGroup(string Name, IReadOnlyList<NavEntry> Entries);

/// <summary>One screen in the navigation rail.</summary>
/// <param name="Text">Its name, a word.</param>
/// <param name="Href">Its address, relative to the base.</param>
/// <param name="Current">Whether it is the screen shown, marked <c>aria-current="page"</c>.</param>
public sealed record NavEntry(string Text, string Href, bool Current);

/// <summary>
/// One plane's sign-in state in the bar (docs/ui/components/Shell): the two
/// planes are shown separately, never as one merged "signed in".
/// </summary>
/// <param name="Name">The plane: <c>Data plane</c> or <c>Management</c>.</param>
/// <param name="Word">What its state is: <c>signed in</c>, <c>signed out</c>, <c>anonymous</c>.</param>
/// <param name="Anonymous">The server runs in anonymous mode: the word is a warn badge.</param>
/// <param name="Absent">The plane holds no token.</param>
/// <param name="Agent">Who the plane's token names, when it is signed in: the agent IRI.</param>
/// <param name="Roles">The roles the plane's token carries, shown on hover or focus.</param>
public sealed record PlaneState(string Name, string Word, bool Anonymous, bool Absent, string? Agent = null, IReadOnlyList<string>? Roles = null);
