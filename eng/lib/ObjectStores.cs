// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The object-store compatibility page as a projection (ADR 0183). Included
// by eng/object-stores.cs, which writes it, and by eng/status.cs and
// eng/release.cs, which check it through lib/Status.cs; not a script of its
// own.
//
// docs/operator/object-stores.md says which stores keep the contract a log
// rests on. It is generated from testdata/object-stores/providers.yaml (the
// stores it lists, and the design limits documented for them) and the result
// records under testdata/object-stores/results/ (what `varve storage verify
// --record` wrote). A provider is:
//
//   verified     a record of it passed
//   unsupported  a record of it failed and none passed, or providers.yaml
//                gives a documented design limit
//   no adapter   providers.yaml names no package for it
//   untested     otherwise
//
// The page is never edited by hand: the check compares it with what these
// files generate, byte for byte.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

sealed record StoreProvider(string Id, string Name, string Adapter, string? Note, string? Unsupported);

sealed record StoreResult(
    string Path, string Provider, string ServerVersion, string Deployment, string Suite, string Date,
    string Racers, string Rounds, bool Passed, string? Case, string? Counterexample, string RunBy, string SignedOffBy);

static class ObjectStoresPage
{
    public const string PagePath = "docs/operator/object-stores.md";

    public const string ProvidersPath = "testdata/object-stores/providers.yaml";

    public const string ResultsPath = "testdata/object-stores/results";

    // The page for these files, or null with the reasons it cannot be made.
    public static string? Generate(Func<string, string?> read, Func<string, IEnumerable<string>> list, List<string> problems)
    {
        int before = problems.Count;
        string? providersText = read(ProvidersPath);

        if (providersText is null)
        {
            problems.Add($"{ProvidersPath} is missing");
            return null;
        }

        List<StoreProvider> providers = Providers(providersText, problems);
        List<StoreResult> results = [];

        foreach (string path in list(ResultsPath).Where(path => path.EndsWith(".yaml", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            if (Result(path, read(path) ?? "", problems) is { } result)
            {
                if (providers.All(provider => provider.Id != result.Provider))
                {
                    problems.Add($"{path}: provider '{result.Provider}' is not in {ProvidersPath}");
                }
                else if (System.IO.Path.GetFileName(path) != $"{result.Provider}-{result.ServerVersion}.yaml")
                {
                    problems.Add($"{path}: a record is named <provider>-<server-version>.yaml, here {result.Provider}-{result.ServerVersion}.yaml");
                }
                else
                {
                    results.Add(result);
                }
            }
        }

        return problems.Count == before ? Render(providers, results) : null;
    }

    // The check eng/status.cs makes: the page is what the files generate.
    public static void Check(Func<string, string?> read, Func<string, IEnumerable<string>> list, List<string> problems, string pagePath = PagePath)
    {
        string? generated = Generate(read, list, problems);

        if (generated is null)
        {
            return;
        }

        string? page = read(pagePath)?.ReplaceLineEndings("\n");

        if (page is null)
        {
            problems.Add($"{pagePath} is missing; `dotnet run eng/object-stores.cs` writes it (ADR 0183)");
        }
        else if (page != generated)
        {
            string[] have = page.Split('\n');
            string[] want = generated.Split('\n');
            int line = 0;

            while (line < have.Length && line < want.Length && have[line] == want[line])
            {
                line++;
            }

            problems.Add($"{pagePath} is not what {ProvidersPath} and the records under {ResultsPath}/ generate, from line {line + 1}; it is never edited by hand: run `dotnet run eng/object-stores.cs` (ADR 0183)");
        }
    }

    private static List<StoreProvider> Providers(string text, List<string> problems)
    {
        List<StoreProvider> providers = [];
        Dictionary<string, string>? current = null;

        void Close()
        {
            if (current is null)
            {
                return;
            }

            string id = current.GetValueOrDefault("id", "");

            if (!Regex.IsMatch(id, "^[a-z0-9-]+$", RegexOptions.CultureInvariant) || !current.ContainsKey("name") || !current.ContainsKey("adapter"))
            {
                problems.Add($"{ProvidersPath}: an entry needs an id of lower-case letters, digits and hyphens, a name and an adapter (id '{id}')");
            }
            else if (providers.Any(provider => provider.Id == id))
            {
                problems.Add($"{ProvidersPath}: '{id}' is listed twice");
            }
            else
            {
                providers.Add(new StoreProvider(id, current["name"], current["adapter"], current.GetValueOrDefault("note"), current.GetValueOrDefault("unsupported")));
            }

            current = null;
        }

        foreach (string raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            if (raw.Trim().Length == 0 || raw.TrimStart().StartsWith('#'))
            {
                continue;
            }

            Match field = Regex.Match(raw, @"^(?<lead>- |  )(?<key>[a-z-]+): (?<value>.+)$", RegexOptions.CultureInvariant);

            if (!field.Success || field.Groups["key"].Value is not ("id" or "name" or "adapter" or "note" or "unsupported"))
            {
                problems.Add($"{ProvidersPath}: '{raw}' is not a field of an entry (id, name, adapter, note, unsupported)");
                continue;
            }

            if (field.Groups["lead"].Value == "- ")
            {
                Close();
                current = [];
            }

            if (current is null || !current.TryAdd(field.Groups["key"].Value, field.Groups["value"].Value.Trim()))
            {
                problems.Add($"{ProvidersPath}: '{raw}' is outside an entry, or repeats a field");
            }
        }

        Close();
        return providers;
    }

    private static StoreResult? Result(string path, string text, List<string> problems)
    {
        Dictionary<string, string> fields = [];

        foreach (string raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            if (raw.Length == 0)
            {
                continue;
            }

            Match field = Regex.Match(raw, @"^(?<key>[a-z-]+): (?:""(?<quoted>(?:[^""\\]|\\.)*)""|(?<bare>[A-Za-z0-9.-]+))$", RegexOptions.CultureInvariant);

            if (!field.Success || !fields.TryAdd(
                field.Groups["key"].Value,
                field.Groups["quoted"].Success ? Regex.Replace(field.Groups["quoted"].Value, @"\\(.)", "$1", RegexOptions.CultureInvariant) : field.Groups["bare"].Value))
            {
                problems.Add($"{path}: '{raw}' is not a line `varve storage verify --record` writes");
                return null;
            }
        }

        if (fields.GetValueOrDefault("format") != "1")
        {
            problems.Add($"{path}: format '{fields.GetValueOrDefault("format", "none")}' is not one this generator reads (1)");
            return null;
        }

        string[] required = ["provider", "server-version", "deployment", "suite", "date", "racers", "rounds", "result", "run-by", "signed-off-by"];
        string[] missing = [.. required.Where(key => !fields.ContainsKey(key))];

        if (missing.Length > 0)
        {
            problems.Add($"{path}: no {string.Join(", ", missing)}");
            return null;
        }

        if (fields["result"] is not ("pass" or "fail")
            || (fields["result"] == "fail") != (fields.ContainsKey("counterexample-case") && fields.ContainsKey("counterexample"))
            || fields["run-by"] is not ("ci" or "maintainer")
            || !DateOnly.TryParseExact(fields["date"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            problems.Add($"{path}: the result is pass, or fail with its counterexample; run-by is ci or maintainer; the date is yyyy-MM-dd");
            return null;
        }

        return new StoreResult(
            path, fields["provider"], fields["server-version"], fields["deployment"], fields["suite"], fields["date"],
            fields["racers"], fields["rounds"], fields["result"] == "pass",
            fields.GetValueOrDefault("counterexample-case"), fields.GetValueOrDefault("counterexample"), fields["run-by"], fields["signed-off-by"]);
    }

    private static string State(StoreProvider provider, List<StoreResult> results) =>
        results.Any(result => result.Provider == provider.Id && result.Passed) ? "verified"
        : provider.Unsupported is not null || results.Any(result => result.Provider == provider.Id) ? "unsupported"
        : provider.Adapter == "none" ? "no adapter"
        : "untested";

    private static string Render(List<StoreProvider> providers, List<StoreResult> results)
    {
        StringBuilder page = new();
        page.Append(Head);
        string[] states = ["verified", "untested", "unsupported", "no adapter"];

        page.Append("| Store | State | Adapter | Evidence |\n|---|---|---|---|\n");

        foreach (string state in states)
        {
            foreach (StoreProvider provider in providers.Where(provider => State(provider, results) == state))
            {
                List<string> evidence = [];

                foreach (StoreResult result in results.Where(result => result.Provider == provider.Id))
                {
                    string run = $"version {result.ServerVersion}, suite {result.Suite}, {result.Racers} racers, {result.Rounds} rounds, {result.Date}, run by {(result.RunBy == "ci" ? "CI" : "the maintainer")} ([record](../../{result.Path}))";
                    evidence.Add(result.Passed ? $"Passed: {run}." : $"**Failed**: {run}. {Sentence(result.Case!)}: {result.Counterexample}.");
                }

                if (provider.Unsupported is { } limit)
                {
                    evidence.Add(limit);
                }

                if (provider.Note is { } note)
                {
                    evidence.Add(note);
                }

                if (evidence.Count == 0)
                {
                    evidence.Add("No record.");
                }

                page.Append(CultureInfo.InvariantCulture, $"| {provider.Name} | {(state == "verified" ? "**verified**" : state)} | {(provider.Adapter == "none" ? "none" : $"`{provider.Adapter}`")} | {string.Join(" ", evidence)} |\n");
            }
        }

        page.Append(Tail);
        return page.ToString();
    }

    private static string Sentence(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    private const string Head =
        """
        # Object stores

        <!-- Generated by `dotnet run eng/object-stores.cs` from testdata/object-stores/. Never edited by hand: `eng/status.cs` fails when it differs (ADR 0183). -->

        Which object stores Varve can keep a dataset in, and the evidence for each.

        Varve asks five operations of an object store (ADR 0134). Two of them
        must hold under concurrency: of many clients that create one name at
        once, exactly one is told it wrote, and of many that replace one
        version at once, exactly one is. The writer epoch and its fence rest on
        those two (ADR 0136). A store that calls itself S3-compatible may not
        keep them, and one that loses such a race lets a log be forked without
        either writer being told.

        **The contract suite is the evidence.** `varve storage verify` runs it:
        the five operations, then both races with sixteen clients.

        **A pass is not a proof.** It means the suite found no counterexample
        in the rounds it ran. A store that loses a race rarely can pass, and
        more rounds make that less likely without ruling it out.

        ## The stores

        """;

    private const string Tail =
        """

        | State | Means |
        |---|---|
        | **verified** | a result record of the suite passing against that version, with no counterexample in its rounds, run by CI or by the maintainer |
        | untested | Varve has an adapter for it, and nobody has added a record |
        | unsupported | a recorded failure, or a design limit its own documentation states |
        | no adapter | Varve has no adapter for it |

        **Untested is not a warning that it fails.** It means run the command
        before you trust it, and send the record.

        ## Verify your own store

        <!-- ci: ci.yml storage-services — the steps that run varve storage verify against Azurite and against RustFS, by web identity -->
        ```
        varve storage verify https://{account}.blob.core.windows.net/{container}
        varve storage verify s3://{bucket} --endpoint https://s3.example.org --region eu-central-1
        ```

        - **The address is a container or a bucket,** with no dataset after it.
          The command reaches it as your identity (ADR 0137): workload
          identity, never a key.
        - **It works in a scratch prefix,** `varve-verify/<random>/`, and removes
          it afterwards. It names anything it could not remove. It needs to
          create, list and delete at the store's root, so an identity scoped to
          one dataset's prefix cannot run it.
        - **Exit code 0 is a pass.** Exit code 1 is a store that broke the
          contract, with the case and what was seen. Do not keep a dataset in
          a store that fails.
        - **`--rounds`** sets how many times each race runs. The default is 8.
          More rounds find a rarer race.
        - **Run it again after you upgrade the store.** A new version of the
          store can handle a race differently, and neither Varve nor the
          record at the store knows that it was upgraded. Run it again after
          a change to how the store is deployed, too: more nodes, another
          consistency setting, a gateway in front of it.

        ## The record at the store

        A pass writes the object `varve-verification` at the store's root: the
        suite's version, the racers and rounds, and when it ran. A failed run
        removes it.

        - **It guards against a mistake, not an adversary.** Anyone who can
          write to the store can write the record. It answers "did someone run
          the suite against this endpoint", so that a store nobody tried, or one
          named by a typing error, is caught.
        - **A record a build cannot read is not a verification.** Run the
          command again with that build.
        - **It describes the store as it was when the command ran.** After an
          upgrade of the store the record is still there and says nothing
          about the new version, so run the command again.

        ## Add a record to this page

        Run the command with `--record`, add the file under
        `testdata/object-stores/results/`, run `dotnet run eng/object-stores.cs`
        and open a pull request. `testdata/object-stores/README.md` has the
        steps.

        """;
}
