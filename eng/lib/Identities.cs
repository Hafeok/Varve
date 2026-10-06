// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The identity map, eng/identities.json (ADR 0087). Included by the gates that
// need it (`#:include lib/Identities.cs`); not a script of its own.
//
// Three kinds of identity:
//
//   humans   a person who may sign off, accept a decision and approve a pull
//            request: a name, the emails they commit and sign off with, their
//            GitHub login, and delegates — other humans, by id, allowed to act
//            for them. A human's delegates change only in that human's own
//            pull request (eng/agent-review.cs).
//   agents   an identity that authors commits for a human: a name, its emails,
//            and the id of its responsible human.
//   exempt   automation whose commit messages cannot be configured, by author
//            name. Closed and explicit; widening it is how a gate stops
//            meaning anything.
//
// An author found in none of them is treated as a human who is not in the map:
// held to the rules for humans, with no delegates.
//
// This is the interim form of the ledger's authority model, and is replaced by
// the ledger's grants when the generator reads the ledger (ADR 0087).

using System.Text.Json;

sealed record Human(string Id, string Name, IReadOnlyList<string> Emails, string? GitHub, IReadOnlyList<string> Delegates);

sealed record Agent(string Id, string Name, IReadOnlyList<string> Emails, string Responsible);

sealed class IdentityMap
{
    public const string RelativePath = "eng/identities.json";

    public required IReadOnlyList<Human> Humans { get; init; }

    public required IReadOnlyList<Agent> Agents { get; init; }

    public required IReadOnlyList<string> Exempt { get; init; }

    public static IdentityMap Parse(string json, List<string> problems)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        JsonElement root = document.RootElement;

        List<Human> humans = [];
        List<Agent> agents = [];
        List<string> exempt = [];

        foreach (JsonElement entry in Array(root, "humans"))
        {
            humans.Add(new Human(
                Text(entry, "id", problems) ?? "",
                Text(entry, "name", problems) ?? "",
                Emails(entry),
                entry.TryGetProperty("github", out JsonElement login) ? login.GetString() : null,
                [.. Array(entry, "delegates").Select(d => d.GetString() ?? "")]));
        }

        foreach (JsonElement entry in Array(root, "agents"))
        {
            agents.Add(new Agent(
                Text(entry, "id", problems) ?? "",
                Text(entry, "name", problems) ?? "",
                Emails(entry),
                Text(entry, "responsible", problems) ?? ""));
        }

        foreach (JsonElement entry in Array(root, "exempt"))
        {
            exempt.Add(Text(entry, "name", problems) ?? "");
        }

        IdentityMap map = new() { Humans = humans, Agents = agents, Exempt = exempt };
        map.Validate(problems);
        return map;
    }

    public static IdentityMap? Load(string repositoryRoot, List<string> problems)
    {
        string path = Path.Combine(repositoryRoot, RelativePath);

        if (!File.Exists(path))
        {
            problems.Add($"no identity map at {RelativePath}");
            return null;
        }

        try
        {
            return Parse(File.ReadAllText(path), problems);
        }
        catch (JsonException exception)
        {
            problems.Add($"{RelativePath} is not valid JSON: {exception.Message}");
            return null;
        }
    }

    public bool IsExempt(string authorName) => Exempt.Contains(authorName, StringComparer.OrdinalIgnoreCase);

    public Agent? AgentByEmail(string email) =>
        Agents.FirstOrDefault(agent => agent.Emails.Contains(email, StringComparer.OrdinalIgnoreCase));

    public Human? HumanById(string id) => Humans.FirstOrDefault(human => human.Id == id);

    public Human? HumanByEmail(string email) =>
        Humans.FirstOrDefault(human => human.Emails.Contains(email, StringComparer.OrdinalIgnoreCase));

    // The humans who may act for an agent: its responsible human and that
    // human's delegates.
    public IReadOnlyList<Human> ActingFor(Agent agent)
    {
        Human? responsible = HumanById(agent.Responsible);

        if (responsible is null)
        {
            return [];
        }

        return [responsible, .. responsible.Delegates.Select(HumanById).OfType<Human>()];
    }

    void Validate(List<string> problems)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<string> emails = new(StringComparer.OrdinalIgnoreCase);

        foreach (string id in Humans.Select(h => h.Id).Concat(Agents.Select(a => a.Id)))
        {
            if (!ids.Add(id))
            {
                problems.Add($"{RelativePath}: the id '{id}' is used twice");
            }
        }

        foreach (string email in Humans.SelectMany(h => h.Emails).Concat(Agents.SelectMany(a => a.Emails)))
        {
            if (!emails.Add(email))
            {
                problems.Add($"{RelativePath}: the email '{email}' belongs to two identities");
            }
        }

        foreach (Agent agent in Agents)
        {
            if (HumanById(agent.Responsible) is null)
            {
                problems.Add($"{RelativePath}: agent '{agent.Id}' names '{agent.Responsible}' as responsible, which is not a human in the map");
            }
        }

        foreach (Human human in Humans)
        {
            foreach (string delegateId in human.Delegates)
            {
                if (delegateId == human.Id || HumanById(delegateId) is null)
                {
                    problems.Add($"{RelativePath}: human '{human.Id}' names '{delegateId}' as a delegate, which is not another human in the map");
                }
            }
        }
    }

    static IEnumerable<JsonElement> Array(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray()
            : [];

    static string? Text(JsonElement element, string name, List<string> problems)
    {
        if (element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text)
        {
            return text;
        }

        problems.Add($"{RelativePath}: an entry has no '{name}'");
        return null;
    }

    static IReadOnlyList<string> Emails(JsonElement element) =>
        [.. Array(element, "emails").Select(e => e.GetString() ?? "")];
}

// A Signed-off-by trailer: "Name <email>".
sealed record SignOff(string Name, string Email)
{
    public static List<SignOff> FromMessage(string message)
    {
        List<SignOff> found = [];

        foreach (string raw in message.Split('\n'))
        {
            string line = raw.Trim();

            if (!line.StartsWith("Signed-off-by:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = line["Signed-off-by:".Length..].Trim();
            int open = value.LastIndexOf('<');
            int close = value.LastIndexOf('>');

            found.Add(open > 0 && close > open
                ? new SignOff(value[..open].Trim(), value[(open + 1)..close].Trim())
                : new SignOff("", value));
        }

        return found;
    }

    public bool Is(string name, string email) =>
        string.Equals(Name, name, StringComparison.Ordinal) && string.Equals(Email, email, StringComparison.OrdinalIgnoreCase);
}
