// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// The two traits every test class of this namespace carries, exactly one
/// each (#143): <see cref="Objects"/> for the object-store suites, which the
/// optional CI leg runs against a storage account on every dispatch, and
/// <see cref="Faults"/> for the failure-injection workloads, which run
/// nightly, on <c>land/**</c> and at a release. <see cref="TraitGuardTests"/>
/// fails a class that carries neither, so no class drops out of CI unseen.
/// </summary>
internal static class ObjectTraits
{
    public const string Name = "Category";

    public const string Objects = "AzureObjects";

    public const string Faults = "AzureObjectsFaults";
}

/// <summary>Every test class in this namespace carries exactly one of the two traits.</summary>
[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class TraitGuardTests
{
    [Fact]
    public void every_test_class_of_the_objects_namespace_carries_exactly_one_of_the_two_traits()
    {
        List<string> wrong = [];

        foreach (Type type in typeof(TraitGuardTests).Assembly.GetTypes())
        {
            if (type.Namespace != typeof(TraitGuardTests).Namespace || !type.IsClass || type.IsAbstract || type.IsNested
                || !type.GetMethods().Any(method => method.IsDefined(typeof(FactAttribute), inherit: true)))
            {
                continue;
            }

            string[] values = [.. type.GetCustomAttributes(typeof(TraitAttribute), inherit: false)
                .Cast<TraitAttribute>()
                .Where(trait => trait.Name == ObjectTraits.Name)
                .Select(trait => trait.Value)];

            if (values.Length != 1 || values[0] is not (ObjectTraits.Objects or ObjectTraits.Faults))
            {
                wrong.Add(type.Name + " [" + string.Join(", ", values) + "]");
            }
        }

        Assert.True(wrong.Count == 0, "These test classes do not carry exactly one of Category=" + ObjectTraits.Objects + " and Category=" + ObjectTraits.Faults + ": " + string.Join("; ", wrong));
    }
}

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
