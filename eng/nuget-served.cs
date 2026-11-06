// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// nuget.org serves the attested package (ADR 0224 point 3).
//
//   dotnet run eng/nuget-served.cs -- <attested.nupkg> <served.nupkg>
//
// A package is attested before it is pushed. nuget.org then adds its
// repository signature, `.signature.p7s`, an entry of its own, so the package
// it serves is not the attested package's bytes, and `gh attestation verify`
// cannot be pointed at it. This is the link between the two: every entry of
// the served package but the signature is the attested package's, byte for
// byte, and the served package has no entry the attested one lacks. The
// signature itself is checked by `dotnet nuget verify`, after this.
//
// Exit codes: 0 the same package, 1 different, 2 could not run.

using System.IO.Compression;

if (args.Length != 2 || !File.Exists(args[0]) || !File.Exists(args[1]))
{
    Console.Error.WriteLine("nuget-served: <attested.nupkg> <served.nupkg>, both existing files.");
    return 2;
}

const string Signature = ".signature.p7s";

using ZipArchive attested = ZipFile.OpenRead(args[0]);
using ZipArchive served = ZipFile.OpenRead(args[1]);

Dictionary<string, ZipArchiveEntry> servedEntries = served.Entries
    .Where(static e => e.FullName != Signature)
    .ToDictionary(static e => e.FullName, StringComparer.Ordinal);

List<string> findings = [];

if (attested.GetEntry(Signature) is not null)
{
    findings.Add($"the attested package already has {Signature}; it was signed before it was attested");
}

if (served.GetEntry(Signature) is null)
{
    findings.Add($"the served package has no {Signature}; nuget.org signs every package it accepts");
}

foreach (ZipArchiveEntry entry in attested.Entries)
{
    if (!servedEntries.Remove(entry.FullName, out ZipArchiveEntry? counterpart))
    {
        findings.Add($"{entry.FullName} is attested and not served");
        continue;
    }

    if (!Bytes(entry).AsSpan().SequenceEqual(Bytes(counterpart)))
    {
        findings.Add($"{entry.FullName} is served with different bytes");
    }
}

foreach (string name in servedEntries.Keys.Order(StringComparer.Ordinal))
{
    findings.Add($"{name} is served and was not attested");
}

string label = Path.GetFileName(args[0]);

if (findings.Count == 0)
{
    Console.WriteLine($"ok  {label}: nuget.org serves the attested package's {attested.Entries.Count} entries, and its own signature");
    return 0;
}

Console.Error.WriteLine($"FAIL: {label}: nuget.org does not serve the attested package:");

foreach (string finding in findings)
{
    Console.Error.WriteLine($"  {finding}");
}

return 1;

static byte[] Bytes(ZipArchiveEntry entry)
{
    using Stream stream = entry.Open();
    using MemoryStream copy = new();
    stream.CopyTo(copy);
    return copy.ToArray();
}
