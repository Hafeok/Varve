// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.WebUi;

/// <summary>
/// Where a problem type's page is (ADR 0119): <c>docs/problems/{name}.md</c>
/// at the commit this build came from, so the page matches the server the
/// Web UI was released with; <c>main</c> when the build names no commit.
/// </summary>
internal static class ProblemDocs
{
    internal static string Page(string name) =>
        "https://github.com/Hafeok/Varve/blob/"
        + (BuildRevision.Commit.Length > 0 ? BuildRevision.Commit : "main")
        + "/docs/problems/" + Uri.EscapeDataString(name) + ".md";
}
