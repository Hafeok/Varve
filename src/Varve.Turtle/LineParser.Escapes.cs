// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Rdf;
using Varve.Turtle.Model;

namespace Varve.Turtle;

/// <summary>
/// The line parser's escape handling, which is
/// <see cref="EscapeDecoder"/>'s with this parser's error reporting around it.
/// </summary>
/// <remarks>
/// The rules live in <see cref="EscapeDecoder"/> because Turtle needs the same
/// ones, and two implementations of UCHAR would be two sets of bugs. What stays
/// here is only the translation into <see cref="Fail"/>.
/// </remarks>
internal ref partial struct LineParser
{
    private bool TryMeasureEscape(int index, bool allowEchar, out int length)
    {
        EscapeDecoder.Allowed allowed = allowEchar
            ? EscapeDecoder.Allowed.UcharAndEchar
            : EscapeDecoder.Allowed.UcharOnly;

        if (EscapeDecoder.TryMeasure(_line, index, allowed, out length, out ParseErrorKind error, out int at, out _))
        {
            return true;
        }

        return Fail(error, at);
    }

    private bool TryDecodeEscaped(int start, int end, bool allowEchar, out TermSpan span)
    {
        EscapeDecoder.Allowed allowed = allowEchar
            ? EscapeDecoder.Allowed.UcharAndEchar
            : EscapeDecoder.Allowed.UcharOnly;

        if (!EscapeDecoder.TryDecode(
            _line, start, end, allowed, _arena, out span, out ParseErrorKind error, out int at))
        {
            return Fail(error, at);
        }

        // An IRI is validated after its escapes are resolved, which is what
        // makes <http://example/ > a bad IRI rather than a good one
        // containing an escape. EscapeDecoder cannot do this: validation needs
        // this parser's options and its IRI error channel.
        if (allowEchar)
        {
            return true;
        }

        ReadOnlySpan<byte> decoded = _arena.Bytes(_line, span);
        return TryValidateIri(decoded, start);
    }
}
