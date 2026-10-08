// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Varve.Server.Commands;

/// <summary>
/// The credential file (ADR 0104): nothing but refresh tokens, each with
/// the server, the issuer and the client it was obtained for. One file under
/// the user's profile, mode 0600 on Unix and refused when wider, protected
/// with DPAPI on Windows. On macOS it is weaker than the Keychain, which the
/// operator guide says; <c>--no-store</c> keeps it empty.
/// </summary>
internal sealed class CredentialFile(string? path)
{
    internal string Path { get; } = path ?? DefaultPath();

    internal static string DefaultPath()
    {
        string root = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
                ? xdg
                : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return System.IO.Path.Combine(root, "varve", "credentials.json");
    }

    internal string? TryRead(Uri server, string authority, string clientId)
    {
        foreach ((string s, string a, string c, string token) in Entries())
        {
            if (s == server.AbsoluteUri && a == authority && c == clientId)
            {
                return token;
            }
        }

        return null;
    }

    internal void Write(Uri server, string authority, string clientId, string refreshToken)
    {
        List<(string, string, string, string)> entries = Entries();
        entries.RemoveAll(e => e.Item1 == server.AbsoluteUri && e.Item2 == authority && e.Item3 == clientId);
        entries.Add((server.AbsoluteUri, authority, clientId, refreshToken));
        Save(entries);
    }

    internal void Remove(Uri server, string authority, string clientId)
    {
        List<(string, string, string, string)> entries = Entries();

        if (entries.RemoveAll(e => e.Item1 == server.AbsoluteUri && e.Item2 == authority && e.Item3 == clientId) > 0)
        {
            Save(entries);
        }
    }

    private List<(string Server, string Authority, string ClientId, string RefreshToken)> Entries()
    {
        List<(string, string, string, string)> entries = [];

        if (!File.Exists(Path))
        {
            return entries;
        }

        if (!OperatingSystem.IsWindows())
        {
            UnixFileMode mode = File.GetUnixFileMode(Path);

            if ((mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite)) != 0)
            {
                throw new Cli.CommandException(Path + " is readable by others; a credential file is mode 0600. Fix its mode or delete it.");
            }
        }

        byte[] bytes = File.ReadAllBytes(Path);

        if (OperatingSystem.IsWindows())
        {
            bytes = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
        }

        using JsonDocument document = JsonDocument.Parse(bytes);

        if (document.RootElement.TryGetProperty("refreshTokens", out JsonElement list))
        {
            foreach (JsonElement entry in list.EnumerateArray())
            {
                entries.Add((entry.GetProperty("server").GetString()!, entry.GetProperty("authority").GetString()!, entry.GetProperty("clientId").GetString()!, entry.GetProperty("refreshToken").GetString()!));
            }
        }

        return entries;
    }

    private void Save(List<(string Server, string Authority, string ClientId, string RefreshToken)> entries)
    {
        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter json = new(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteStartArray("refreshTokens");

            foreach ((string server, string authority, string clientId, string token) in entries)
            {
                json.WriteStartObject();
                json.WriteString("server", server);
                json.WriteString("authority", authority);
                json.WriteString("clientId", clientId);
                json.WriteString("refreshToken", token);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        byte[] bytes = buffer.WrittenSpan.ToArray();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);

        if (OperatingSystem.IsWindows())
        {
            File.WriteAllBytes(Path, ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
            return;
        }

        FileStreamOptions options = new()
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        };

        using FileStream stream = new(Path, options);
        stream.Write(bytes);
        File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
