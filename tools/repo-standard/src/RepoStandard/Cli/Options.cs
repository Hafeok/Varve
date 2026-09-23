// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace RepoStandard.Cli;

/// <summary>A command line that cannot be run.</summary>
internal sealed class UsageException : Exception
{
    public UsageException(string message)
        : base(message)
    {
    }

    public UsageException()
    {
    }

    public UsageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The parsed command line.</summary>
internal sealed record Options(
    string Command,
    string Owner,
    string Name,
    string File,
    string Token,
    Uri ApiUrl,
    Uri GraphQlUrl,
    bool AllowStatusReset,
    bool Force)
{
    private const string DefaultApi = "https://api.github.com";

    /// <inheritdoc />
    /// <remarks>Overridden so that no formatting of the options can print the token.</remarks>
    public override string ToString() => $"Options {{ Command = {Command}, Repo = {Owner}/{Name}, File = {File} }}";

    /// <summary>Parses the arguments, falling back to the environment where the usage says so.</summary>
    public static Options Parse(string[] args, Func<string, string?> environment)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            return Empty("help");
        }

        if (args[0] is "--version" or "version")
        {
            return Empty("version");
        }

        string command = args[0];
        if (command is not ("export" or "plan" or "apply" or "check"))
        {
            throw new UsageException($"unknown command '{command}'");
        }

        string? repo = null;
        string file = "repo-standard.yaml";
        string? token = null;
        string? apiUrl = null;
        string? graphQlUrl = null;
        bool allowStatusReset = false;
        bool force = false;

        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--repo":
                    repo = Value(args, ref i);
                    break;
                case "--file":
                    file = Value(args, ref i);
                    break;
                case "--token":
                    token = Value(args, ref i);
                    break;
                case "--api-url":
                    apiUrl = Value(args, ref i);
                    break;
                case "--graphql-url":
                    graphQlUrl = Value(args, ref i);
                    break;
                case "--allow-status-reset" when command == "apply":
                    allowStatusReset = true;
                    break;
                case "--force" when command == "export":
                    force = true;
                    break;
                default:
                    throw new UsageException($"'{arg}' is not an option of {command}");
            }
        }

        repo ??= NonEmpty(environment("GITHUB_REPOSITORY"))
            ?? throw new UsageException("--repo OWNER/NAME is required (or GITHUB_REPOSITORY)");

        string[] parts = repo.Split('/');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            throw new UsageException($"--repo takes OWNER/NAME, not '{repo}'");
        }

        token ??= NonEmpty(environment("GITHUB_TOKEN"))
            ?? throw new UsageException("a token is required: set GITHUB_TOKEN, or pass --token");

        Uri api = ParseUrl(apiUrl ?? NonEmpty(environment("GITHUB_API_URL")) ?? DefaultApi, "--api-url");
        Uri graphQl = ParseUrl(graphQlUrl ?? NonEmpty(environment("GITHUB_GRAPHQL_URL")) ?? GraphQlFor(api), "--graphql-url");

        return new Options(command, parts[0], parts[1], file, token, api, graphQl, allowStatusReset, force);
    }

    /// <summary>
    /// GitHub.com serves GraphQL at /graphql beside the REST root; GitHub
    /// Enterprise Server serves REST at /api/v3 and GraphQL at /api/graphql.
    /// </summary>
    private static string GraphQlFor(Uri api)
    {
        string root = api.ToString().TrimEnd('/');
        return root.EndsWith("/api/v3", StringComparison.OrdinalIgnoreCase)
            ? root[..^"/v3".Length] + "/graphql"
            : root + "/graphql";
    }

    private static Uri ParseUrl(string value, string option) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme is "https" or "http"
            ? uri
            : throw new UsageException($"{option} takes an absolute http(s) URL, not '{value}'");

    private static string Value(string[] args, ref int i) =>
        i + 1 < args.Length ? args[++i] : throw new UsageException($"{args[i]} needs a value");

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static Options Empty(string command) =>
        new(command, string.Empty, string.Empty, string.Empty, string.Empty, new Uri(DefaultApi), new Uri(DefaultApi), false, false);
}
