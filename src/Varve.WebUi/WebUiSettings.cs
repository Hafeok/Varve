// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.WebUi;

/// <summary>What the composition root decided at boot: the server, and whether it is in anonymous mode.</summary>
/// <param name="Server">The server's base address (ADR 0202).</param>
/// <param name="Anonymous">No sign-in: the server runs with <c>Auth:Mode</c> <c>Anonymous</c>.</param>
public sealed record WebUiSettings(Uri Server, bool Anonymous);
