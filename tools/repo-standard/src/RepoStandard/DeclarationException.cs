// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace RepoStandard;

/// <summary>A declaration that cannot be read, merged or validated.</summary>
internal sealed class DeclarationException : Exception
{
    public DeclarationException(string message)
        : base(message)
    {
    }

    public DeclarationException()
    {
    }

    public DeclarationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
