// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RepoStandard.Json;
using RepoStandard.Yaml;

namespace RepoStandard.Declaration;

/// <summary>
/// Reads a declaration: its <c>extends</c> chain, merged base first, then
/// <c>${owner}</c> and <c>${repo}</c> filled in, then validated.
/// </summary>
/// <remarks>
/// <para>
/// <c>extends</c> is a path, relative to the file that names it, or an
/// <c>https://</c> URL. A base fetched from a URL is fetched with no
/// credentials: the token is for GitHub's API and goes nowhere else. A relative
/// <c>extends</c> inside a fetched base resolves against its URL.
/// </para>
/// <para>
/// Merging, the repository's file over its base: mappings merge key by key; a
/// key set to null in the override is removed, which makes it unmanaged; a
/// keyed list (<see cref="KeyedLists"/>) merges item by item on its key, an
/// override item replacing the base item whole, and an item carrying
/// <c>absent: true</c> removing it; any other list, and any scalar, is replaced.
/// </para>
/// </remarks>
internal sealed class DeclarationLoader
{
    /// <summary>The lists merged by key, and the key of each.</summary>
    public static readonly IReadOnlyDictionary<string, string> KeyedLists = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["labels"] = "name",
        ["rulesets"] = "name",
        ["environments"] = "name",
        ["projects"] = "title",
        ["discussions.categories"] = "name",
    };

    private const int MaxDepth = 10;

    private readonly Func<Uri, CancellationToken, Task<string>> _fetch;

    /// <param name="fetch">Fetches a base declaration from an https URL, with no credentials.</param>
    public DeclarationLoader(Func<Uri, CancellationToken, Task<string>> fetch)
    {
        _fetch = fetch;
    }

    /// <summary>Loads, merges, interpolates and validates.</summary>
    public async Task<JsonObject> LoadAsync(string location, string owner, string repo, CancellationToken cancellationToken)
    {
        JsonObject merged = await LoadChainAsync(location, [], cancellationToken).ConfigureAwait(false);
        JsonObject interpolated = (JsonObject)Interpolate(merged, owner, repo, location)!;
        DeclarationSchema.Validate(interpolated, location);
        return interpolated;
    }

    /// <summary>Loads a single YAML text with no extends, for tests and exports.</summary>
    public static JsonObject Parse(string text, string source)
    {
        JsonNode? node = YamlJson.Parse(text, source);
        return node switch
        {
            null => [],
            JsonObject obj => obj,
            _ => throw new DeclarationException($"{source}: a declaration is a mapping at the top level."),
        };
    }

    /// <summary>Merges an override onto a base, by the rules above.</summary>
    public static JsonObject Merge(JsonObject baseline, JsonObject overrides, string path = "")
    {
        JsonObject result = (JsonObject)baseline.DeepClone();

        foreach (KeyValuePair<string, JsonNode?> property in overrides)
        {
            string childPath = path.Length == 0 ? property.Key : path + "." + property.Key;

            if (property.Value is null)
            {
                result.Remove(property.Key);
            }
            else if (KeyedLists.TryGetValue(childPath, out string? key) && property.Value is JsonArray items
                && result[property.Key] is JsonArray baseItems)
            {
                result[property.Key] = MergeKeyed(baseItems, items, key);
            }
            else if (property.Value is JsonObject child && result[property.Key] is JsonObject baseChild)
            {
                result[property.Key] = Merge(baseChild, child, childPath);
            }
            else
            {
                result[property.Key] = property.Value.DeepClone();
            }
        }

        return result;
    }

    private async Task<JsonObject> LoadChainAsync(string location, List<string> seen, CancellationToken cancellationToken)
    {
        if (seen.Contains(location, StringComparer.Ordinal))
        {
            throw new DeclarationException($"extends forms a cycle: {string.Join(" -> ", seen)} -> {location}");
        }

        if (seen.Count >= MaxDepth)
        {
            throw new DeclarationException($"extends is nested more than {MaxDepth} deep at {location}");
        }

        seen.Add(location);
        string text = await ReadAsync(location, cancellationToken).ConfigureAwait(false);
        JsonObject declaration = Parse(text, location);

        if (!declaration.TryGetPropertyValue("extends", out JsonNode? extends))
        {
            return RemoveAbsent(declaration);
        }

        declaration.Remove("extends");
        if (extends is not JsonValue value || value.GetValueKind() is not JsonValueKind.String)
        {
            throw new DeclarationException($"{location}: extends is a path or an https URL.");
        }

        string baseLocation = Resolve(location, value.GetValue<string>());
        JsonObject baseline = await LoadChainAsync(baseLocation, seen, cancellationToken).ConfigureAwait(false);
        return RemoveAbsent(Merge(baseline, declaration));
    }

    private async Task<string> ReadAsync(string location, CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(location, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https")
        {
            if (uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new DeclarationException($"{location}: a base declaration is fetched over https only.");
            }

            return await _fetch(uri, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return await File.ReadAllTextAsync(location, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new DeclarationException($"{location}: {exception.Message}");
        }
    }

    private static string Resolve(string from, string target)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out Uri? absolute) && absolute.Scheme is "http" or "https")
        {
            return absolute.ToString();
        }

        if (Uri.TryCreate(from, UriKind.Absolute, out Uri? fromUri) && fromUri.Scheme is "http" or "https")
        {
            return new Uri(fromUri, target).ToString();
        }

        if (Path.IsPathRooted(target))
        {
            return target;
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(from)) ?? ".";
        return Path.GetFullPath(Path.Combine(directory, target));
    }

    private static JsonArray MergeKeyed(JsonArray baseItems, JsonArray overrides, string key)
    {
        List<JsonNode?> result = [.. baseItems.Select(item => item?.DeepClone())];

        foreach (JsonNode? item in overrides)
        {
            string? name = JsonTree.Str(item, key);
            int index = name is null ? -1 : result.FindIndex(existing => JsonTree.Str(existing, key) == name);

            if (index >= 0)
            {
                result[index] = item?.DeepClone();
            }
            else
            {
                result.Add(item?.DeepClone());
            }
        }

        JsonArray array = [];
        foreach (JsonNode? item in result)
        {
            array.Add(item);
        }

        return array;
    }

    /// <summary>Drops keyed-list items marked <c>absent: true</c> once merging is done.</summary>
    private static JsonObject RemoveAbsent(JsonObject declaration)
    {
        foreach ((string path, _) in KeyedLists)
        {
            string[] parts = path.Split('.');
            JsonNode? parent = declaration;
            foreach (string part in parts[..^1])
            {
                parent = parent?[part];
            }

            if (parent is JsonObject obj && obj[parts[^1]] is JsonArray items)
            {
                foreach (JsonNode? item in items.ToList())
                {
                    if (JsonTree.Bool(item, "absent") == true)
                    {
                        items.Remove(item);
                    }
                }
            }
        }

        return declaration;
    }

    private static JsonNode? Interpolate(JsonNode? node, string owner, string repo, string source)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                JsonObject copy = [];
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    copy[property.Key] = Interpolate(property.Value, owner, repo, source);
                }

                return copy;
            }

            case JsonArray array:
            {
                JsonArray copy = [];
                foreach (JsonNode? item in array)
                {
                    copy.Add(Interpolate(item, owner, repo, source));
                }

                return copy;
            }

            case JsonValue value when value.GetValueKind() is JsonValueKind.String:
                return JsonValue.Create(Substitute(value.GetValue<string>(), owner, repo, source));

            default:
                return node?.DeepClone();
        }
    }

    /// <summary>
    /// Fills <c>${owner}</c> and <c>${repo}</c>; <c>$${</c> is a literal
    /// <c>${</c>. Any other <c>${...}</c> is an error, so that a typo is not
    /// written to GitHub as text.
    /// </summary>
    internal static string Substitute(string text, string owner, string repo, string source)
    {
        if (!text.Contains("${", StringComparison.Ordinal))
        {
            return text;
        }

        StringBuilder builder = new();
        int index = 0;
        while (index < text.Length)
        {
            if (text[index] == '$' && index + 2 < text.Length && text[index + 1] == '$' && text[index + 2] == '{')
            {
                builder.Append("${");
                index += 3;
                continue;
            }

            if (text[index] == '$' && index + 1 < text.Length && text[index + 1] == '{')
            {
                int close = text.IndexOf('}', index);
                if (close < 0)
                {
                    throw new DeclarationException($"{source}: '${{' is not closed in \"{text}\"; write $${{ for a literal ${{.");
                }

                string name = text[(index + 2)..close];
                builder.Append(name switch
                {
                    "owner" => owner,
                    "repo" => repo,
                    _ => throw new DeclarationException(
                        $"{source}: '${{{name}}}' is not a variable; the variables are ${{owner}} and ${{repo}}, and $${{ is a literal ${{."),
                });
                index = close + 1;
                continue;
            }

            builder.Append(text[index]);
            index++;
        }

        return builder.ToString();
    }
}
