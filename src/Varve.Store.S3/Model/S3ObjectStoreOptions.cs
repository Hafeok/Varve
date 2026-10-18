// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Net.Http;

namespace Varve.Store.S3.Model;

/// <summary>Where an S3 bucket is, and how to reach it.</summary>
public sealed class S3ObjectStoreOptions
{
    /// <summary>
    /// The service's address: <c>https://s3.{region}.amazonaws.com</c> for
    /// Amazon S3, which is addressed by virtual host, or a server's own, which
    /// is addressed by path (<c>{endpoint}/{bucket}/{key}</c>).
    /// </summary>
    public required Uri Endpoint { get; init; }

    /// <summary>The bucket.</summary>
    public required S3Bucket Bucket { get; init; }

    /// <summary>The region signatures are scoped to.</summary>
    public S3Region Region { get; init; } = S3Region.Default;

    /// <summary>
    /// The STS endpoint web identity is exchanged at: Amazon's for Amazon S3,
    /// the server's own otherwise. Unused with <see cref="DevelopmentKey"/>.
    /// </summary>
    public Uri? StsEndpoint { get; init; }

    /// <summary>A static key, accepted only when <see cref="Endpoint"/> is a loopback address (ADR 0137).</summary>
    public S3DevelopmentKey? DevelopmentKey { get; init; }

    /// <summary>
    /// The clock signatures are dated by and a credential's expiry is read
    /// from. Required: the adapter never reads the machine's clock itself
    /// (ADR 0011).
    /// </summary>
    public required TimeProvider Clock { get; init; }

    /// <summary>The handler requests go through, when the host has one of its own. The caller disposes it.</summary>
    public HttpMessageHandler? Handler { get; init; }
}
