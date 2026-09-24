// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace RepoStandard;

/// <summary>A write repo-standard declines to make, with the reason.</summary>
internal sealed class RepoStandardException : Exception
{
    public RepoStandardException(string message)
        : base(message)
    {
    }

    public RepoStandardException()
    {
    }

    public RepoStandardException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
