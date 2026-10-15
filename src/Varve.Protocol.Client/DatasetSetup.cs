// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Varve.Protocol.Client;

/// <summary>
/// A dataset as an application declares it (ADR 0130): its name and storage,
/// its settings, the baseline it starts from and the migrations that are not
/// idempotent by nature. <see cref="SparqlHttpClient.ApplyAsync"/> makes the
/// dataset so, and applying the same setup again commits nothing.
/// </summary>
/// <remarks>
/// Grants are not part of a setup: an application cannot grant itself
/// permissions, and they are the server's configuration (ADR 0107).
/// </remarks>
public sealed class DatasetSetup
{
    /// <summary>The setup of the dataset named <paramref name="name"/>.</summary>
    public DatasetSetup(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    /// <summary>The dataset's name, the last segment of <c>/datasets/{name}/</c>.</summary>
    public string Name { get; }

    /// <summary>The storage the dataset is created in when it does not exist; an existing dataset keeps its own.</summary>
    public SetupStorage Storage { get; init; } = SetupStorage.File;

    /// <summary>The settings, as the JSON object the settings resource takes (ADR 0118), or null to leave them as they are.</summary>
    public string? Settings { get; init; }

    /// <summary>
    /// The baseline, added in order by the Graph Store Protocol's <c>POST</c>:
    /// one commit each the first time, none after. A baseline only adds, never
    /// retracts, so what the application wrote to a baseline's graph survives
    /// every apply; retractions are migrations, recorded.
    /// </summary>
    public IList<BaselineSource> Baselines { get; } = [];

    /// <summary>The migrations, applied in order, each once: its update and its record are one commit.</summary>
    public IList<SetupMigration> Migrations { get; } = [];
}

/// <summary>The storage a dataset is created in (ADR 0118).</summary>
public enum SetupStorage
{
    /// <summary>Files under the server's datasets root: durable.</summary>
    File = 0,

    /// <summary>Memory: gone when the server stops.</summary>
    Memory = 1,

    /// <summary>A prefix in the server's object store: durable as the store is (ADR 0139).</summary>
    ObjectStorage = 2,
}

/// <summary>
/// One source of a dataset's baseline (ADR 0130): RDF in a syntax Varve reads,
/// added to one graph, never replacing it. A source holding a blank node is refused before any
/// request: its nodes would be new on every write (ADR 0098), and the setup
/// would commit every time it is applied.
/// </summary>
public sealed class BaselineSource
{
    private readonly Func<CancellationToken, ValueTask<byte[]>> _content;

    /// <summary>A source named <paramref name="name"/>, for <paramref name="graph"/> (null: the default graph), in <paramref name="mediaType"/>, read by <paramref name="content"/>.</summary>
    public BaselineSource(string name, string? graph, string mediaType, Func<CancellationToken, ValueTask<byte[]>> content)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mediaType);
        ArgumentNullException.ThrowIfNull(content);
        Name = name;
        Graph = graph;
        MediaType = mediaType;
        _content = content;
    }

    /// <summary>What the source is called in a refusal and in the result.</summary>
    public string Name { get; }

    /// <summary>The graph it is written to, an absolute IRI, or null for the default graph.</summary>
    public string? Graph { get; }

    /// <summary>Its media type: <c>text/turtle</c>, <c>application/n-triples</c>, <c>application/rdf+xml</c> or <c>application/ld+json</c>.</summary>
    public string MediaType { get; }

    /// <summary>The source's bytes.</summary>
    public ValueTask<byte[]> ReadAsync(CancellationToken cancellationToken) => _content(cancellationToken);

    /// <summary>A source held in memory.</summary>
    public static BaselineSource FromBytes(string name, string? graph, string mediaType, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new BaselineSource(name, graph, mediaType, _ => ValueTask.FromResult(content));
    }

    /// <summary>
    /// A source opened by <paramref name="open"/> each time it is read: a
    /// file, or an embedded resource (<c>typeof(T).Assembly.GetManifestResourceStream(…)</c>).
    /// </summary>
    public static BaselineSource FromStream(string name, string? graph, string mediaType, Func<Stream> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        return new BaselineSource(name, graph, mediaType, async cancellationToken =>
        {
            Stream stream = open() ?? throw new InvalidOperationException("The baseline source " + name + " opened no stream.");

            await using (stream.ConfigureAwait(false))
            {
                using MemoryStream copy = new();
                await stream.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
                return copy.ToArray();
            }
        });
    }

    /// <summary>A source in a file, its media type from its extension unless given.</summary>
    public static BaselineSource FromFile(string path, string? graph, string? mediaType = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string type = mediaType ?? MediaTypeOf(path)
            ?? throw new ArgumentException("The media type of " + path + " is not known from its extension; give it.", nameof(mediaType));
        return new BaselineSource(Path.GetFileName(path), graph, type, async cancellationToken => WithoutByteOrderMark(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    /// The bytes without a leading UTF-8 byte order mark: an editor writes one,
    /// and none of the syntaxes has a place for it.
    /// </summary>
    public static byte[] WithoutByteOrderMark(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? content[3..] : content;
    }

    /// <summary>The media type a file's extension names, or null.</summary>
    public static string? MediaTypeOf(string path) => Path.GetExtension(path).ToUpperInvariant() switch
    {
        ".TTL" => "text/turtle",
        ".NT" => "application/n-triples",
        ".RDF" or ".OWL" or ".XML" => "application/rdf+xml",
        ".JSONLD" => "application/ld+json",
        _ => null,
    };
}

/// <summary>
/// A change that is not idempotent by nature (ADR 0130): a retraction, a
/// rename, a split. Applied once, in the setup's order, as one SPARQL Update
/// request together with its record in <c>varve:setup</c>.
/// </summary>
/// <param name="Id">Unique within the setup, and never reused: its record is how the dataset remembers it ran.</param>
/// <param name="Update">One SPARQL Update request.</param>
public sealed record SetupMigration(string Id, string Update);

/// <summary>What <see cref="SparqlHttpClient.ApplyAsync"/> did (ADR 0130).</summary>
public sealed class SetupResult
{
    internal SetupResult(bool created, bool settingsChanged, IReadOnlyList<string> baselines, IReadOnlyList<string> migrations, long position)
    {
        Created = created;
        SettingsChanged = settingsChanged;
        BaselinesCommitted = baselines;
        MigrationsApplied = migrations;
        Position = position;
    }

    /// <summary>Whether the dataset was created.</summary>
    public bool Created { get; }

    /// <summary>Whether the settings were changed: one commit.</summary>
    public bool SettingsChanged { get; }

    /// <summary>The baseline sources that committed, by name: one commit each.</summary>
    public IReadOnlyList<string> BaselinesCommitted { get; }

    /// <summary>The migrations applied, by id: one commit each.</summary>
    public IReadOnlyList<string> MigrationsApplied { get; }

    /// <summary>The dataset's position once the setup was applied.</summary>
    public long Position { get; }

    /// <summary>Whether anything committed: false for a setup already applied.</summary>
    public bool Committed => SettingsChanged || BaselinesCommitted.Count > 0 || MigrationsApplied.Count > 0;
}

/// <summary>
/// A setup that could not be applied (ADR 0130): the step that failed, the
/// HTTP status when the server refused it, and its problem body. What the
/// steps before it committed stands; the failed step committed nothing.
/// </summary>
public sealed class SetupException : Exception
{
    /// <summary>A failure at <paramref name="step"/>, with the server's refusal when there was one.</summary>
    public SetupException(string step, string message, int? status, string? problem)
        : base(step + ": " + message + (status is int code ? " (" + code.ToString(System.Globalization.CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(problem) ? ")" : ", " + problem + ")") : string.Empty))
    {
        Step = step;
        Status = status;
        Problem = problem;
    }

    /// <summary>Creates one with a message.</summary>
    public SetupException(string message)
        : base(message)
    {
        Step = string.Empty;
    }

    /// <summary>Creates one with a message and the error underneath.</summary>
    public SetupException(string message, Exception inner)
        : base(message, inner)
    {
        Step = string.Empty;
    }

    /// <summary>Creates one with no message.</summary>
    public SetupException()
    {
        Step = string.Empty;
    }

    /// <summary>The step: <c>dataset</c>, <c>settings</c>, <c>baseline {name}</c> or <c>migration {id}</c>.</summary>
    public string Step { get; }

    /// <summary>The HTTP status of the refusal, when the server refused.</summary>
    public int? Status { get; }

    /// <summary>The refusal's body, an RFC 9457 problem as JSON, when there was one.</summary>
    public string? Problem { get; }
}
