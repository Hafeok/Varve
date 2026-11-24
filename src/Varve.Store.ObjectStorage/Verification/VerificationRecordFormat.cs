// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Globalization;
using System.Text.Json;
using Varve.Store.ObjectStorage.Model;

namespace Varve.Store.ObjectStorage;

/// <summary>
/// A verification record's bytes (ADR 0183): JSON an operator can read,
/// written and read by hand so that nothing reflects. A record of another
/// format, or one that does not read, is not a verification (ADR 0217).
/// </summary>
internal static class VerificationRecordFormat
{
    private const int Version = 1;

    public static byte[] Write(VerificationRecord record)
    {
        ArrayBufferWriter<byte> buffer = new();

        // One line ending on every platform: the record's bytes are its format's (ADR 0216).
        using (Utf8JsonWriter json = new(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            json.WriteStartObject();
            json.WriteNumber("format", Version);
            json.WriteNumber("suite", record.Suite.Value);
            json.WriteNumber("racers", ObjectStoreVerification.Racers.Value);
            json.WriteNumber("rounds", record.Rounds.Value);

            // To the second, in UTC, with a Z: an operator reads it, and nothing decides by it.
            json.WriteString("at", record.VerifiedAt.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            json.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static VerificationRecord Read(ReadOnlySpan<byte> bytes)
    {
        try
        {
            Utf8JsonReader reader = new(bytes);
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out JsonElement format) || format.GetInt32() != Version)
            {
                return new VerificationRecord { Understood = false };
            }

            return new VerificationRecord
            {
                Understood = true,
                Suite = new VerificationSuite(root.GetProperty("suite").GetInt32()),
                Rounds = new VerificationRounds(root.GetProperty("rounds").GetInt32()),
                VerifiedAt = new VerificationTime(DateTimeOffset.Parse(root.GetProperty("at").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)),
            };
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or ArgumentException or System.Collections.Generic.KeyNotFoundException)
        {
            return new VerificationRecord { Understood = false };
        }
    }
}
