// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.WebUi.Organisms;

/// <summary>
/// One commit as the commit list shows it (docs/ui/components/CommitList):
/// display values a page derives from the commits resource. The organism
/// knows nothing of the client's types (ADR 0206 point 4).
/// </summary>
/// <param name="Position">The commit's position, as written.</param>
/// <param name="Href">Where its delta is shown.</param>
/// <param name="What">Who made it: the agent, or the instance and kind.</param>
/// <param name="Cause">When, and the request or management position that caused it.</param>
/// <param name="Added">How many assertions.</param>
/// <param name="Removed">How many retractions.</param>
/// <param name="IsNew">Arrived through the live tail a moment ago.</param>
public sealed record CommitEntry(string Position, string Href, string What, string Cause, long Added, long Removed, bool IsNew);
