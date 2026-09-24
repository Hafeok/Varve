// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace RepoStandard.GitHub;

/// <summary>
/// A request GitHub refused or could not answer. The message carries the
/// method, the path, the status and GitHub's own message, and never anything
/// from the request's headers.
/// </summary>
internal sealed class GitHubException : Exception
{
    public GitHubException(Endpoint endpoint, string request, int status, string message)
        : base(status == 0 ? $"{request}: {message}" : $"{request}: {status} {message}")
    {
        Endpoint = endpoint;
        Status = status;
    }

    public GitHubException()
    {
        Endpoint = Endpoints.GraphQl;
    }

    public GitHubException(string message)
        : base(message)
    {
        Endpoint = Endpoints.GraphQl;
    }

    public GitHubException(string message, Exception innerException)
        : base(message, innerException)
    {
        Endpoint = Endpoints.GraphQl;
    }

    /// <summary>The endpoint that failed.</summary>
    public Endpoint Endpoint { get; }

    /// <summary>The HTTP status, or 0 when there was no response.</summary>
    public int Status { get; }
}
