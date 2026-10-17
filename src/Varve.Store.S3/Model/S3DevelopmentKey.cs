// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Store.S3.Model;

/// <summary>
/// A static access key for an S3-compatible server on a loopback address,
/// and only there (ADR 0137, LoopbackDevelopmentKey): CI's test server, or a
/// store running on the same machine. It renders without its secret.
/// </summary>
public sealed class S3DevelopmentKey
{
    /// <summary>A key pair.</summary>
    /// <exception cref="ArgumentException">Either part is empty.</exception>
    public S3DevelopmentKey(string accessKeyId, string secretAccessKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(accessKeyId);
        ArgumentException.ThrowIfNullOrEmpty(secretAccessKey);
        AccessKeyId = accessKeyId;
        SecretAccessKey = secretAccessKey;
    }

    internal string AccessKeyId { get; }

    internal string SecretAccessKey { get; }

    /// <summary>Renders as the key's id, never its secret.</summary>
    public override string ToString() => "development key " + AccessKeyId;
}
