// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// What counts as an approval comment (ADR 0087, amended 2026-11-12; #290).
// Included by the two scripts that read approvals, eng/agent-review.cs and
// eng/land.cs (`#:include lib/Approvals.cs`); not a script of its own.
//
// An approval is a pull request conversation comment that is the approval
// line and nothing else:
//
//   - WHOLE BODY. The body, trimmed of surrounding whitespace, is the word
//     `approve`, one space, and the head's full sha: 40 lowercase hexadecimal
//     characters. A delegate's ends ` (delegate)` (ADR 0239). A second line,
//     a quotation mark, a code span, a sentence around it: any other
//     character and the comment approves nothing. Before this rule the line
//     was matched anywhere in a comment, and a session's status report that
//     quoted it for the current head was read as the maintainer's approval.
//   - FULL SHA. A prefix names no head. The caller compares the sha this
//     returns with the head for equality.
//   - NEVER EDITED. A comment whose `updated_at` differs from its
//     `created_at` approves nothing: an approval is given once, for one head,
//     and a comment edited into the line was not written as one. A comment
//     with either timestamp missing approves nothing either.
//
// Who wrote the comment is not decided here. eng/agent-review.cs holds the
// author to the identity map; eng/land.cs leaves that verdict to it.

using System.Text.Json;
using System.Text.RegularExpressions;

static partial class Approvals
{
    [GeneratedRegex(@"\Aapprove (?<sha>[0-9a-f]{40})(?<delegate> \(delegate\))?\z", RegexOptions.CultureInvariant)]
    private static partial Regex Line();

    // The full sha the comment approves, or null when it approves nothing.
    public static string? Head(string? body, string? createdAt, string? updatedAt)
    {
        if (body is null || string.IsNullOrEmpty(createdAt) || string.IsNullOrEmpty(updatedAt)
            || !string.Equals(createdAt, updatedAt, StringComparison.Ordinal))
        {
            return null;
        }

        Match match = Line().Match(body.Trim());
        return match.Success ? match.Groups["sha"].Value : null;
    }

    // The same, for a comment as GitHub's REST API serves it
    // (`issues/<n>/comments`): `body`, `created_at`, `updated_at`.
    public static string? Head(JsonElement comment) => comment.ValueKind != JsonValueKind.Object
        ? null
        : Head(Text(comment, "body"), Text(comment, "created_at"), Text(comment, "updated_at"));

    private static string? Text(JsonElement comment, string property) =>
        comment.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
