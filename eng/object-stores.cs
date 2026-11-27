// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The object-store compatibility page, generated (ADR 0183).
//
//   dotnet run eng/object-stores.cs              writes docs/operator/object-stores.md
//   dotnet run eng/object-stores.cs -- --check   fails when the page is not what would be written
//
// The page is the projection of testdata/object-stores/providers.yaml and the
// result records under testdata/object-stores/results/, which `varve storage
// verify --record` writes. lib/ObjectStores.cs says what each state means.
// eng/status.cs makes the same check on every pipeline run, so a page edited
// by hand, or a record added without regenerating it, is red.
//
// Exit codes: 0 written or current, 1 findings, 2 could not run.
//
// See docs/adr/0183-object-store-verification-and-the-compatibility-page.md.

// CA2266 is off for this file only, for the reason eng/dco.cs gives (ADR 0087).
#:include lib/ObjectStores.cs
#:property NoWarn=$(NoWarn);CA2266

string root = FindRepositoryRoot();
bool check = false;

foreach (string argument in args)
{
    if (argument == "--check")
    {
        check = true;
    }
    else
    {
        Console.Error.WriteLine($"object-stores: '{argument}' is not an option (see the header of eng/object-stores.cs).");
        return 2;
    }
}

string? Read(string path) => File.Exists(Path.Combine(root, path)) ? File.ReadAllText(Path.Combine(root, path)) : null;

IEnumerable<string> List(string directory) => Directory.Exists(Path.Combine(root, directory))
    ? Directory.EnumerateFiles(Path.Combine(root, directory), "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
    : [];

List<string> problems = [];

if (check)
{
    ObjectStoresPage.Check(Read, List, problems);
}
else if (ObjectStoresPage.Generate(Read, List, problems) is { } page)
{
    File.WriteAllText(Path.Combine(root, ObjectStoresPage.PagePath), page);
    File.WriteAllText(Path.Combine(root, ObjectStoresPage.StatesPath), ObjectStoresPage.GenerateStates(Read, List, problems)!);
    Console.WriteLine($"ok  wrote {ObjectStoresPage.PagePath} and {ObjectStoresPage.StatesPath}");
    return 0;
}

if (problems.Count == 0)
{
    Console.WriteLine($"ok  {ObjectStoresPage.PagePath} is what the provider list and the result records generate");
    return 0;
}

Console.WriteLine();
Console.WriteLine($"FAIL: {problems.Count} finding(s):");
problems.ForEach(problem => Console.WriteLine($"  {problem}"));
return 1;

static string FindRepositoryRoot()
{
    DirectoryInfo? directory = new(Directory.GetCurrentDirectory());

    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Varve.slnx")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Could not find the repository root (no Varve.slnx above the current directory).");
}
