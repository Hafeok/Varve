// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BlendSpike;

/// <summary>
/// The code-fact projector: a repository's files in, N-Quads out. It reads
/// project files, the repository's props, the central package versions, the
/// public API baselines, the decision sets and, when asked, the decision
/// declarations in source. It runs no compiler: a fact that needs one is a
/// gap, listed in the findings.
/// </summary>
internal static partial class Projector
{
    public const string CodeGraph = "urn:varve-spike:g/code";
    public const string LedgerGraph = "urn:varve-spike:g/ledger";
    private const string Code = "urn:varve-spike:code#";
    private const string Ledger = "urn:ledger:ns#";
    private const string Prov = "http://www.w3.org/ns/prov#";
    private const string Blend = "urn:varve-spike:blend#";
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";
    private const string XsdBoolean = "http://www.w3.org/2001/XMLSchema#boolean";
    private const string XsdDateTime = "http://www.w3.org/2001/XMLSchema#dateTime";

    /// <summary>Whether the projector reads a path at all.</summary>
    public static bool Reads(string path, bool declarations) =>
        !path.StartsWith("tests/w3c/", StringComparison.Ordinal)
        && (path.EndsWith(".csproj", StringComparison.Ordinal)
            || path is "Directory.Build.props" or "Directory.Packages.props"
            || path.EndsWith("/PublicAPI.Shipped.txt", StringComparison.Ordinal)
            || path.EndsWith("/PublicAPI.Unshipped.txt", StringComparison.Ordinal)
            || (path.StartsWith("docs/decisions/", StringComparison.Ordinal) && path.EndsWith(".md", StringComparison.Ordinal))
            || (declarations && path.StartsWith("src/", StringComparison.Ordinal) && path.EndsWith(".cs", StringComparison.Ordinal)));

    /// <summary>Projects the files to sorted, distinct N-Quads lines.</summary>
    public static SortedSet<string> Project(IReadOnlyDictionary<string, string> files)
    {
        SortedSet<string> quads = new(StringComparer.Ordinal);
        Dictionary<string, string> projectIri = ProjectIris(files.Keys);

        foreach ((string path, string text) in files)
        {
            if (path.EndsWith(".csproj", StringComparison.Ordinal))
            {
                ProjectFile(quads, path, text, projectIri);
            }
            else if (path == "Directory.Build.props")
            {
                RepositoryProps(quads, text);
            }
            else if (path == "Directory.Packages.props")
            {
                PackageVersions(quads, text);
            }
            else if (path.EndsWith(".txt", StringComparison.Ordinal))
            {
                PublicApi(quads, path, text, projectIri);
            }
            else if (path.StartsWith("docs/decisions/", StringComparison.Ordinal))
            {
                DecisionSet(quads, path, text);
            }
            else if (path.EndsWith(".cs", StringComparison.Ordinal))
            {
                Declarations(quads, path, text, projectIri);
            }
        }

        return quads;
    }

    /// <summary>
    /// A project's IRI is its name, or its name and path when two projects in
    /// the repository share a name (the dependency-register fixtures do).
    /// </summary>
    private static Dictionary<string, string> ProjectIris(IEnumerable<string> paths)
    {
        List<string> projects = [.. paths.Where(static p => p.EndsWith(".csproj", StringComparison.Ordinal))];
        Dictionary<string, int> counts = projects
            .GroupBy(static p => Path.GetFileNameWithoutExtension(p), StringComparer.Ordinal)
            .ToDictionary(static g => g.Key, static g => g.Count(), StringComparer.Ordinal);
        Dictionary<string, string> iris = new(StringComparer.Ordinal);
        foreach (string path in projects)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            iris[path] = counts[name] == 1
                ? "urn:varve-spike:pkg:" + name
                : "urn:varve-spike:pkg:" + name + "@" + Uri.EscapeDataString(path);
        }

        return iris;
    }

    private static void ProjectFile(SortedSet<string> quads, string path, string text, Dictionary<string, string> iris)
    {
        string pkg = iris[path];
        string name = Path.GetFileNameWithoutExtension(path);
        Add(quads, pkg, Rdf, Iri(Code + "Project"), CodeGraph);
        Add(quads, pkg, Code + "name", Literal(name), CodeGraph);
        Add(quads, pkg, Code + "path", Literal(path), CodeGraph);
        if (name.EndsWith(".Tests", StringComparison.Ordinal))
        {
            Add(quads, pkg, Code + "testAssembly", Typed("true", XsdBoolean), CodeGraph);
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(text);
        }
        catch (System.Xml.XmlException)
        {
            Add(quads, pkg, Code + "unreadable", Typed("true", XsdBoolean), CodeGraph);
            return;
        }

        string? layer = Property(document, "ArchLayer");
        if (layer is not null && int.TryParse(layer, NumberStyles.None, CultureInfo.InvariantCulture, out int n))
        {
            // The layer declaration is the input space R1 reads: a code fact,
            // whose authority is the commit that landed it, not an acceptance.
            string declaration = "urn:varve-spike:layerdecl:" + pkg["urn:varve-spike:pkg:".Length..];
            Add(quads, declaration, Rdf, Iri(Blend + "MentalSpace"), CodeGraph);
            Add(quads, declaration, Blend + "level", Typed("0", XsdInteger), CodeGraph);
            Add(quads, declaration, Code + "declares", Iri(pkg), CodeGraph);
            Add(quads, declaration, Code + "layer", Typed(n.ToString(CultureInfo.InvariantCulture), XsdInteger), CodeGraph);
            Add(quads, pkg, Code + "layer", Typed(n.ToString(CultureInfo.InvariantCulture), XsdInteger), CodeGraph);
        }
        else if (layer is not null)
        {
            Add(quads, pkg, Code + "layerText", Literal(layer), CodeGraph);
        }

        if (Property(document, "ArchCompositionRoot") is { } root)
        {
            Add(quads, pkg, Code + "compositionRoot", Literal(root), CodeGraph);
        }

        if (Property(document, "OutputType") is { } output)
        {
            Add(quads, pkg, Code + "outputType", Literal(output), CodeGraph);
        }

        if (Property(document, "IsPackable") is { } packable)
        {
            Add(quads, pkg, Code + "packable", Literal(packable), CodeGraph);
        }

        string directory = Path.GetDirectoryName(path) ?? "";
        foreach (XElement item in document.Descendants().Where(static e => e.Name.LocalName == "ProjectReference"))
        {
            string? include = (string?)item.Attribute("Include");
            if (include is null)
            {
                continue;
            }

            string target = Normalise(directory, include);
            if (!iris.TryGetValue(target, out string? targetIri))
            {
                Add(quads, pkg, Code + "unresolvedReference", Literal(include), CodeGraph);
                continue;
            }

            // A reference taken as an analyzer is not a library reference.
            string? outputItemType = (string?)item.Attribute("OutputItemType") ?? ChildText(item, "OutputItemType");
            if (outputItemType == "Analyzer")
            {
                Add(quads, pkg, Code + "analyzerReference", Iri(targetIri), CodeGraph);
                continue;
            }

            Add(quads, pkg, Code + "references", Iri(targetIri), CodeGraph);
            if (item.Attribute("Condition") is not null || item.Parent?.Attribute("Condition") is not null)
            {
                Add(quads, pkg, Code + "conditionalReference", Iri(targetIri), CodeGraph);
            }
        }

        foreach (XElement item in document.Descendants().Where(static e => e.Name.LocalName == "PackageReference"))
        {
            if ((string?)item.Attribute("Include") is { } id)
            {
                Add(quads, pkg, Code + "packageReference", Iri("urn:varve-spike:nuget:" + id), CodeGraph);
            }
        }

        foreach (XElement item in document.Descendants().Where(static e => e.Name.LocalName == "InternalsVisibleTo"))
        {
            if ((string?)item.Attribute("Include") is { } friend)
            {
                Add(quads, pkg, Code + "internalsVisibleTo", Literal(friend), CodeGraph);
            }
        }
    }

    private static void RepositoryProps(SortedSet<string> quads, string text)
    {
        const string repo = "urn:varve-spike:repo";
        XDocument document = XDocument.Parse(text);
        if (Property(document, "ArchFamily") is { } family)
        {
            Add(quads, repo, Code + "archFamily", Literal(family), CodeGraph);
        }

        foreach (XElement item in document.Descendants().Where(static e => e.Name.LocalName == "PackageReference"))
        {
            if ((string?)item.Attribute("Include") is { } id)
            {
                Add(quads, repo, Code + "globalPackageReference", Iri("urn:varve-spike:nuget:" + id), CodeGraph);
            }
        }
    }

    private static void PackageVersions(SortedSet<string> quads, string text)
    {
        XDocument document = XDocument.Parse(text);
        foreach (XElement item in document.Descendants().Where(static e => e.Name.LocalName == "PackageVersion"))
        {
            if ((string?)item.Attribute("Include") is not { } id)
            {
                continue;
            }

            string package = "urn:varve-spike:nuget:" + id;
            if ((string?)item.Attribute("Version") is { } version)
            {
                Add(quads, package, Code + "version", Literal(version), CodeGraph);
            }

            if ((string?)item.Attribute("Adr") is { } adr)
            {
                Add(quads, package, Code + "admittedByAdr", Literal(adr), CodeGraph);
            }
        }
    }

    private static void PublicApi(SortedSet<string> quads, string path, string text, Dictionary<string, string> iris)
    {
        string directory = Path.GetDirectoryName(path) ?? "";
        string? project = iris.Keys.FirstOrDefault(p => (Path.GetDirectoryName(p) ?? "") == directory);
        if (project is null)
        {
            return;
        }

        int lines = text.Split('\n').Count(static l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#'));
        string predicate = path.EndsWith("Shipped.txt", StringComparison.Ordinal) && !path.EndsWith("Unshipped.txt", StringComparison.Ordinal)
            ? Code + "publicApiShipped"
            : Code + "publicApiUnshipped";
        Add(quads, iris[project], predicate, Typed(lines.ToString(CultureInfo.InvariantCulture), XsdInteger), CodeGraph);
    }

    /// <summary>
    /// A decision set's front matter as ledger nodes: a decision per key, a
    /// version per statement, an acceptance per accepted-by. The version IRI
    /// is a SHA-256 of set, key and statement; it is NOT the ledger's
    /// canonical version hash (ledger-protocol section 4), which needs fields
    /// a decision set does not have.
    /// </summary>
    private static void DecisionSet(SortedSet<string> quads, string path, string text)
    {
        if (!text.StartsWith("---", StringComparison.Ordinal))
        {
            return;
        }

        int end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0)
        {
            return;
        }

        string? set = null;
        string? adr = null;
        Dictionary<string, string>? current = null;
        List<Dictionary<string, string>> entries = [];
        foreach (string raw in text[3..end].Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            Match field = FieldPattern().Match(line);
            if (!field.Success)
            {
                continue;
            }

            string name = field.Groups["name"].Value;
            string value = field.Groups["value"].Value.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1];
            }

            if (field.Groups["dash"].Success && name == "key")
            {
                current = new(StringComparer.Ordinal) { ["key"] = value };
                entries.Add(current);
            }
            else if (current is not null && field.Groups["indent"].Value.Length >= 4)
            {
                current[name] = value;
            }
            else if (name == "set")
            {
                set = value;
            }
            else if (name is "adr" or "origin")
            {
                adr = value;
            }
        }

        if (set is null)
        {
            return;
        }

        foreach (Dictionary<string, string> entry in entries)
        {
            string key = entry["key"];
            string statement = entry.GetValueOrDefault("statement", "");
            string decision = "urn:dec:varve/" + key;
            string version = "urn:sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(set + "\n" + key + "\n" + statement)));
            Add(quads, decision, Rdf, Iri(Ledger + "Decision"), LedgerGraph);
            Add(quads, decision, Ledger + "id", Literal("dec:varve/" + key), LedgerGraph);
            Add(quads, decision, Ledger + "namespace", Literal("varve"), LedgerGraph);
            Add(quads, decision, Ledger + "key", Literal(key), LedgerGraph);
            Add(quads, version, Rdf, Iri(Ledger + "DecisionVersion"), LedgerGraph);
            Add(quads, version, Ledger + "ofDecision", Iri(decision), LedgerGraph);
            Add(quads, version, Ledger + "statement", Literal(statement), LedgerGraph);
            Add(quads, version, Ledger + "set", Iri("urn:ledger-set:" + set), LedgerGraph);
            if (adr is not null)
            {
                Add(quads, version, Ledger + "basedOn", Literal("adr:" + adr), LedgerGraph);
            }

            if (entry.TryGetValue("accepted-by", out string? by) && entry.TryGetValue("accepted-at", out string? at))
            {
                string acceptance = "urn:acc:varve/" + key + "/" + version["urn:sha256:".Length..][..16];
                Add(quads, acceptance, Rdf, Iri(Ledger + "Acceptance"), LedgerGraph);
                Add(quads, acceptance, Ledger + "ofDecision", Iri(decision), LedgerGraph);
                Add(quads, acceptance, Ledger + "signsVersion", Iri(version), LedgerGraph);
                Add(quads, acceptance, Ledger + "scope", Literal("version"), LedgerGraph);
                Add(quads, acceptance, Prov + "wasAttributedTo", Iri(by), LedgerGraph);
                Add(quads, acceptance, Prov + "generatedAtTime", Typed(at, XsdDateTime), LedgerGraph);
            }

            if (entry.TryGetValue("revoked-at", out string? revoked))
            {
                Add(quads, version, Code + "revokedAt", Typed(revoked, XsdDateTime), LedgerGraph);
            }
        }
    }

    /// <summary>
    /// <c>[Contract]</c>, <c>[HotPath]</c> and <c>[DesignDecision]</c> citing
    /// a decision, by pattern. Without a compiler the symbol is the next type
    /// or member declaration after the attribute, which is a guess.
    /// </summary>
    private static void Declarations(SortedSet<string> quads, string path, string text, Dictionary<string, string> iris)
    {
        string? project = iris.Keys
            .Where(p => path.StartsWith((Path.GetDirectoryName(p) ?? "") + "/", StringComparison.Ordinal))
            .OrderByDescending(static p => p.Length)
            .FirstOrDefault();
        if (project is null)
        {
            return;
        }

        foreach (Match citation in CitationPattern().Matches(text))
        {
            string attribute = citation.Groups["attribute"].Value;
            string key = citation.Groups["key"].Value;
            Match symbol = SymbolPattern().Match(text, citation.Index + citation.Length);
            string name = symbol.Success && symbol.Index - citation.Index < 400 ? symbol.Groups["name"].Value : "?";
            string kind = symbol.Success && symbol.Index - citation.Index < 400 ? symbol.Groups["kind"].Value : "?";
            string node = "urn:varve-spike:declaration:" + Uri.EscapeDataString(path) + "#" + citation.Index.ToString(CultureInfo.InvariantCulture);
            Add(quads, node, Code + "attribute", Literal(attribute), CodeGraph);
            Add(quads, node, Code + "cites", Iri("urn:dec:varve/" + key), CodeGraph);
            Add(quads, node, Code + "inProject", Iri(iris[project]), CodeGraph);
            Add(quads, node, Code + "symbol", Literal(name), CodeGraph);
            Add(quads, node, Code + "symbolKind", Literal(kind), CodeGraph);
            if (citation.Groups["role"].Success)
            {
                Add(quads, node, Code + "role", Literal(citation.Groups["role"].Value), CodeGraph);
            }
        }
    }

    private static string? Property(XDocument document, string name) =>
        document.Descendants().FirstOrDefault(e => e.Name.LocalName == name && e.Parent?.Name.LocalName == "PropertyGroup")?.Value.Trim();

    private static string? ChildText(XElement element, string name) =>
        element.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();

    private static string Normalise(string directory, string include)
    {
        string combined = Path.Combine(directory, include.Replace('\\', '/')).Replace('\\', '/');
        Stack<string> parts = new();
        foreach (string part in combined.Split('/'))
        {
            if (part == "..")
            {
                _ = parts.TryPop(out _);
            }
            else if (part is not "." and not "")
            {
                parts.Push(part);
            }
        }

        return string.Join('/', parts.Reverse());
    }

    private static void Add(SortedSet<string> quads, string subject, string predicate, string @object, string graph) =>
        quads.Add("<" + subject + "> <" + predicate + "> " + @object + " <" + graph + "> .");

    private static string Iri(string iri) => "<" + iri + ">";

    private static string Literal(string value) => "\"" + Escape(value) + "\"";

    private static string Typed(string value, string datatype) => "\"" + Escape(value) + "\"^^<" + datatype + ">";

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal);

    [GeneratedRegex(@"^(?<indent>\s*)(?<dash>-\s+)?(?<name>[a-z][a-z-]*):\s*(?<value>.*)$")]
    private static partial Regex FieldPattern();

    [GeneratedRegex(@"\[(?<attribute>Contract|HotPath|DesignDecision)\(typeof\((?:[A-Za-z0-9_]+\.)*[A-Za-z0-9_]+\.(?<key>[A-Za-z0-9_]+)\)(?:[^\]]*?Role\s*=\s*""(?<role>[^""]*)"")?[^\]]*\]")]
    private static partial Regex CitationPattern();

    [GeneratedRegex(@"\b(?<kind>interface|class|struct|record|enum)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)|\b[A-Za-z_][A-Za-z0-9_<>,\[\]?]*\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?<kind>\()")]
    private static partial Regex SymbolPattern();
}
