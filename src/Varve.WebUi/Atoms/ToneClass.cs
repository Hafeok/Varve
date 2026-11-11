// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.WebUi.Atoms;

// A tone's modifier in bundle.css.
internal static class ToneClass
{
    internal static string Of(Tone tone) => tone switch
    {
        Tone.Ok => "ok",
        Tone.Warn => "warn",
        Tone.Critical => "critical",
        Tone.Past => "past",
        _ => "neutral",
    };
}
