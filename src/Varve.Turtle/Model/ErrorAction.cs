// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Turtle.Model;

/// <summary>What the parser should do after an error has been reported.</summary>
public enum ErrorAction : byte
{
    /// <summary>End the parse. This is what happens with no error handler.</summary>
    Stop,

    /// <summary>Resume at the byte after the next end of line.</summary>
    Continue,
}
