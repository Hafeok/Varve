// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The docs site's generator (ADR 0247).
//
//   dotnet run --project eng/site -- check [--root <repository>]
//   dotnet run --project eng/site -- check-svg <file>…
//   dotnet run --project eng/site -- build --out <directory> --commit <sha> [--chrome <path>] [--root <repository>] [--cache <directory>]
//
// check reads every page the site carries and fails on a relative link whose
// file does not exist or whose anchor no heading of that file makes, unless
// eng/site/link-exemptions.txt names that link and the decision that allows
// it. It needs no browser, so it runs in eng/ci.cs.
//
// build writes the site: every page as HTML under a Content Security Policy
// with no script source, each Mermaid diagram drawn in Chrome to an SVG file
// of its own and checked to hold no script and reach no other origin, and
// each package's API reference from its PublicAPI baselines. It runs check
// first, and fails on what check fails on. Its output depends only on the
// repository, the commit named and the browser, so two builds are compared
// byte for byte in CI.
//
// Exit codes: 0 done, 1 a finding, 2 could not run.

using Varve.Site;

// check-svg <file>…: the checks every diagram passes before it is written,
// run on files, so their failure path is proven on fixtures.
if (args.Length > 1 && args[0] == "check-svg")
{
    int findings = 0;

    foreach (string file in args[1..])
    {
        foreach (string finding in MermaidRenderer.Findings(File.ReadAllText(file)))
        {
            Console.Error.WriteLine($"varve-site: {file} {finding}");
            findings++;
        }
    }

    Console.WriteLine($"varve-site: {args.Length - 1} SVG files, {findings} findings.");
    return findings == 0 ? 0 : 1;
}

if (args.Length == 0 || args[0] is not ("check" or "build"))
{
    Console.Error.WriteLine("varve-site: check [--root <repository>] | check-svg <file>… | build --out <directory> --commit <sha> [--chrome <path>] [--root <repository>] [--cache <directory>]");
    return 2;
}

string root = ".";
string? output = null;
string? commit = null;
string? chrome = null;
string? cache = null;

for (int i = 1; i < args.Length; i++)
{
    string? value = i + 1 < args.Length ? args[i + 1] : null;

    switch (args[i])
    {
        case "--root" when value is not null: root = value; i++; break;
        case "--out" when value is not null: output = value; i++; break;
        case "--commit" when value is not null: commit = value; i++; break;
        case "--chrome" when value is not null: chrome = value; i++; break;
        case "--cache" when value is not null: cache = value; i++; break;
        default:
            Console.Error.WriteLine($"varve-site: unknown or incomplete argument '{args[i]}'.");
            return 2;
    }
}

root = Path.GetFullPath(root);

if (!Directory.Exists(Path.Combine(root, "docs")))
{
    Console.Error.WriteLine($"varve-site: {root} has no docs/.");
    return 2;
}

Repository repository = new(root);
SiteMap map = SiteMap.Of(repository);
LinkReport links;

try
{
    links = LinkCheck.Run(repository, map);
}
catch (SiteException e)
{
    Console.Error.WriteLine($"varve-site: {e.Message}");
    return 2;
}

links.Print(Console.Out, Console.Error);

if (args[0] == "check")
{
    return links.Failed ? 1 : 0;
}

if (links.Failed)
{
    Console.Error.WriteLine("varve-site: not built, because the links above are broken.");
    return 1;
}

if (output is null || commit is null)
{
    Console.Error.WriteLine("varve-site: build needs --out and --commit, and --chrome when a page has a diagram.");
    return 2;
}

if (!System.Text.RegularExpressions.Regex.IsMatch(commit, "^[0-9a-f]{40}$"))
{
    Console.Error.WriteLine($"varve-site: --commit '{commit}' is not a full commit sha.");
    return 2;
}

if (chrome is not null && !File.Exists(chrome))
{
    Console.Error.WriteLine($"varve-site: no browser at {chrome}.");
    return 2;
}

cache ??= Path.Combine(root, "artifacts", "site-cache");
output = Path.GetFullPath(output);

try
{
    return await SiteBuild.RunAsync(repository, map, output, commit, chrome, cache);
}
catch (SiteException e)
{
    Console.Error.WriteLine($"varve-site: {e.Message}");
    return 1;
}
