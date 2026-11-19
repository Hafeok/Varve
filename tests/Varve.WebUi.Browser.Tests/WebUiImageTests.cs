// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace Varve.WebUi.Browser.Tests;

/// <summary>
/// The Web UI the container image ships and serves by default (ADR 0245),
/// in Chrome against a running container, not a bundle in this process:
/// <c>VARVE_IMAGE_ANONYMOUS</c> is the address of the image run with only
/// <c>serve --anonymous</c>, and <c>VARVE_IMAGE_NOT_CONFIGURED</c> of the
/// image run with OIDC and no client for the Web UI. Each test skips without
/// its address, and fails instead with <c>VARVE_IMAGE_TESTS_REQUIRED</c> set.
/// </summary>
public sealed class WebUiImageTests
{
    // The screens that read nothing a fresh container lacks.
    private static readonly (string Path, string Rendered)[] Screens =
    [
        ("datasets", "table.vw-table tbody td"),
        ("health", "text=Readiness"),
        ("fleet", "text=This instance in the system dataset"),
    ];

    [Fact]
    public async Task The_images_console_is_served_anonymous_with_no_csp_violation_and_passes_axe()
    {
        (Uri ui, string chrome) = Image("VARVE_IMAGE_ANONYMOUS");
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { ExecutablePath = chrome });
        IPage page = await browser.NewPageAsync();
        List<string> violations = WebUiBrowserTests.Watch(page);
        List<string> findings = [];

        foreach ((string path, string rendered) in Screens)
        {
            await page.GotoAsync(ui + path);
            await page.WaitForSelectorAsync(rendered, new() { Timeout = 60_000 });
            findings.AddRange(await WebUiBrowserTests.AuditAsync(page, "image " + path));
        }

        Assert.Equal(["Data plane anonymous", "Management anonymous"], await WebUiBrowserTests.Planes(page));
        Assert.True(findings.Count == 0, string.Join(Environment.NewLine, findings));
        Assert.Empty(violations);
    }

    [Fact]
    public async Task The_images_console_with_oidc_and_no_client_says_sign_in_is_not_configured()
    {
        (Uri ui, string chrome) = Image("VARVE_IMAGE_NOT_CONFIGURED");
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { ExecutablePath = chrome });
        IPage page = await browser.NewPageAsync();
        List<string> violations = WebUiBrowserTests.Watch(page);

        await page.GotoAsync(ui + "datasets");
        await page.WaitForSelectorAsync("text=Sign-in is not configured", new() { Timeout = 60_000 });

        Assert.Equal(["Data plane not configured", "Management not configured"], await WebUiBrowserTests.Planes(page));
        Assert.Contains("Varve:WebUi:Auth:ClientId", await page.InnerTextAsync(".vw-notice__body"), StringComparison.Ordinal);
        Assert.Empty(await WebUiBrowserTests.AuditAsync(page, "image not configured"));
        Assert.Empty(violations);
    }

    private static (Uri Ui, string Chrome) Image(string variable)
    {
        string? address = Environment.GetEnvironmentVariable(variable);
        string? chrome = Environment.GetEnvironmentVariable("VARVE_CHROME");

        if (string.IsNullOrEmpty(address) || string.IsNullOrEmpty(chrome) || !File.Exists(chrome))
        {
            string why = variable + " (a running image's address) and VARVE_CHROME (a Chrome) are needed.";

            if (Environment.GetEnvironmentVariable("VARVE_IMAGE_TESTS_REQUIRED") == "1")
            {
                Assert.Fail(why);
            }

            Assert.Skip(why);
        }

        return (new Uri(address.TrimEnd('/') + "/ui/"), chrome);
    }
}
