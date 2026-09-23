// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Text;

namespace RepoStandard.Cli;

/// <summary>
/// A line-buffered writer that masks the token in whatever passes through it.
/// </summary>
/// <remarks>
/// The token is placed on the Authorization header and nowhere else, and the
/// tests prove no output carries it. This is the second line of defence: if a
/// future message ever formatted it, it would print as <c>***</c>. It is not
/// how the first line is kept.
/// </remarks>
internal sealed class RedactingWriter : TextWriter
{
    private const string Mask = "***";

    private readonly TextWriter _inner;
    private readonly string _secret;
    private readonly StringBuilder _pending = new();

    public RedactingWriter(TextWriter inner, string secret)
        : base(inner.FormatProvider)
    {
        _inner = inner;
        _secret = secret;
    }

    public override Encoding Encoding => _inner.Encoding;

    /// <summary>The text with every occurrence of the secret masked.</summary>
    public static string Redact(string text, string secret) =>
        string.IsNullOrEmpty(secret) ? text : text.Replace(secret, Mask, StringComparison.Ordinal);

    public override void Write(char value)
    {
        _pending.Append(value);
        if (value == '\n')
        {
            Drain();
        }
    }

    public override void Write(string? value)
    {
        if (value is null)
        {
            return;
        }

        _pending.Append(value);
        if (value.Contains('\n', StringComparison.Ordinal))
        {
            Drain();
        }
    }

    public override void Flush()
    {
        Drain();
        _inner.Flush();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Flush();
        }

        base.Dispose(disposing);
    }

    private void Drain()
    {
        _inner.Write(Redact(_pending.ToString(), _secret));
        _pending.Clear();
    }
}
