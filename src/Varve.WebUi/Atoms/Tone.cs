// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.WebUi.Atoms;

/// <summary>
/// The five tones a state renders in (docs/ui/README.md, Colour), each with its
/// own mark shape. An atom takes a tone; which state has which tone is
/// <see cref="Varve.WebUi.Tones"/>' to say, never an atom's (ADR 0206 point 5).
/// </summary>
public enum Tone
{
    /// <summary>Healthy: a filled disc.</summary>
    Ok,

    /// <summary>Needs attention: a triangle.</summary>
    Warn,

    /// <summary>Failed: a square.</summary>
    Critical,

    /// <summary>Information, no judgement: a hollow ring.</summary>
    Neutral,

    /// <summary>A view pinned as of a position: two bands.</summary>
    Past,
}
