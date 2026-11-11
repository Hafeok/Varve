// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace Varve.WebUi;

/// <summary>
/// How the Web UI writes an instant (docs/ui/README.md, Content): UTC, to the
/// second, ISO 8601, as the server's logs and the change feed do.
/// </summary>
internal static class Times
{
    internal static string Utc(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>How long before <paramref name="now"/>, in the largest whole unit: "11 s ago", "2 min ago", "18 h ago", "3 d ago".</summary>
    internal static string Ago(DateTimeOffset at, DateTimeOffset now)
    {
        TimeSpan since = now - at;

        return since < TimeSpan.Zero ? "in the future"
            : since < TimeSpan.FromMinutes(1) ? Whole(since.TotalSeconds) + " s ago"
            : since < TimeSpan.FromHours(1) ? Whole(since.TotalMinutes) + " min ago"
            : since < TimeSpan.FromHours(48) ? Whole(since.TotalHours) + " h ago"
            : Whole(since.TotalDays) + " d ago";
    }

    private static string Whole(double value) => ((long)value).ToString(CultureInfo.InvariantCulture);
}
