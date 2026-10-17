// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using Xunit;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// The address of a storage service CI starts beside the tests (ADR 0099: the
/// workflow starts it, a test reads where it is and skips when it is absent).
/// <c>VARVE_STORAGE_LEGS_REQUIRED</c> names the legs a workflow requires, which
/// turns the skip into a failure there.
/// </summary>
internal static class ServiceLegs
{
    public static string Require(string leg, string variable)
    {
        string? value = Environment.GetEnvironmentVariable(variable);

        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        string[] required = (Environment.GetEnvironmentVariable("VARVE_STORAGE_LEGS_REQUIRED") ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (required.Contains(leg, StringComparer.Ordinal))
        {
            Assert.Fail("The " + leg + " leg is required here (VARVE_STORAGE_LEGS_REQUIRED) and " + variable + " is not set.");
        }

        Assert.Skip("The " + leg + " leg needs " + variable + " (ADR 0134).");
        return string.Empty;
    }

    /// <summary>A name no other run shares, for a container or a prefix in a shared service.</summary>
    public static string Run() => "t" + Guid.NewGuid().ToString("N")[..20];
}
