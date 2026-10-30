// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.WebUi.Atoms;

/// <summary>A button's weight (docs/ui/components/Button).</summary>
public enum ButtonKind
{
    /// <summary>Quiet, the default.</summary>
    Quiet,

    /// <summary>The act the view exists for, at most once per view.</summary>
    Primary,

    /// <summary>An act that stops service, outlined.</summary>
    Danger,

    /// <summary>The confirming button inside a dangerous act's dialog.</summary>
    DangerSolid,
}
