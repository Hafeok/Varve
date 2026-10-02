// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;

namespace Varve.Store.Log;

/// <summary>
/// The directory of a file-backed dataset, as a normalised absolute path:
/// relative paths resolved against the current directory, <c>.</c> and
/// <c>..</c> segments removed, and no trailing separator but the root's.
/// </summary>
/// <remarks>
/// ADR 0074, <c>DirectoriesAreWrappers</c>. A host converts from a string at
/// its edge; no <c>string</c> path appears on <see cref="FileStorage"/> or
/// <see cref="Dataset"/>. Equality is of the normalised text, ordinal; whether
/// one directory is inside another is <see cref="KeyStoreDirectory.IsWithin"/>,
/// which knows about case on Windows. There is no conversion to or from
/// <c>string</c> but the constructor and <see cref="Value"/> (DD0015).
/// </remarks>
public readonly record struct DatasetDirectory
{
    /// <summary>A dataset directory, normalised.</summary>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null, empty, or not a path.</exception>
    public DatasetDirectory(string path) => Value = DirectoryPath.Normalise(path);

    /// <summary>The normalised absolute path.</summary>
    public string Value { get; }

    /// <summary>Renders as the path.</summary>
    public override string ToString() => Value;
}

/// <summary>
/// The directory of a key store, as a normalised absolute path (see
/// <see cref="DatasetDirectory"/>), which must never be inside a dataset
/// directory (specification §2, §9; ADR 0018).
/// </summary>
/// <remarks>
/// ADR 0074, <c>DirectoriesAreWrappers</c>. No key store exists before
/// erasure mode (milestone 9); the file backend checks this one at open all the
/// same, so that the refusal exists before anything could need it.
/// </remarks>
public readonly record struct KeyStoreDirectory
{
    /// <summary>A key store directory, normalised.</summary>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null, empty, or not a path.</exception>
    public KeyStoreDirectory(string path) => Value = DirectoryPath.Normalise(path);

    /// <summary>The normalised absolute path.</summary>
    public string Value { get; }

    /// <summary>
    /// Whether this directory is the dataset directory or anywhere below it.
    /// Paths are compared segment by segment — so <c>/data/ds2</c> is not
    /// inside <c>/data/ds</c> — case-insensitively on Windows and
    /// case-sensitively elsewhere. Symbolic links are not resolved.
    /// </summary>
    public bool IsWithin(DatasetDirectory dataset) =>
        DirectoryPath.IsWithin(Value, dataset.Value, OperatingSystem.IsWindows());

    /// <summary>Renders as the path.</summary>
    public override string ToString() => Value;
}

/// <summary>The rule the two directory wrappers share.</summary>
internal static class DirectoryPath
{
    internal static string Normalise(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception error) when (error is NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("'" + path + "' is not a directory path.", nameof(path), error);
        }
    }

    /// <summary>Whether <paramref name="inner"/> is <paramref name="outer"/> or below it, both normalised.</summary>
    internal static bool IsWithin(string inner, string outer, bool ignoreCase)
    {
        string[] innerParts = Split(inner);
        string[] outerParts = Split(outer);

        if (outerParts.Length > innerParts.Length)
        {
            return false;
        }

        StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        for (int i = 0; i < outerParts.Length; i++)
        {
            if (!string.Equals(innerParts[i], outerParts[i], comparison))
            {
                return false;
            }
        }

        return true;
    }

    private static string[] Split(string path) =>
        path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
}
