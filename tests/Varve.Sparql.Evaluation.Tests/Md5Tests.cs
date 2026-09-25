// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using CsCheck;
using Varve.Sparql.Evaluation.Expressions;
using Xunit;

namespace Varve.Sparql.Evaluation.Tests;

/// <summary>The package's own MD5 (<c>sparql-evaluation.md</c> §12.4): RFC 1321 §A.5's vectors, and agreement with the platform's where it has one.</summary>
public class Md5Tests
{
    [Theory]
    [InlineData("", "d41d8cd98f00b204e9800998ecf8427e")]
    [InlineData("a", "0cc175b9c0f1b6a831c399e269772661")]
    [InlineData("abc", "900150983cd24fb0d6963f7d28e17f72")]
    [InlineData("message digest", "f96b697d7cb7938d525a2f31aaf161d0")]
    [InlineData("abcdefghijklmnopqrstuvwxyz", "c3fcd3d76192e4007dfb496cca67e13b")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789", "d174ab98d277d9f5a5611c2c9f419d9f")]
    [InlineData("12345678901234567890123456789012345678901234567890123456789012345678901234567890", "57edf4a22be3c955ac49da2e2107b67a")]
    public void The_rfc_1321_a5_vectors(string message, string digest) =>
        Assert.Equal(digest, Convert.ToHexStringLower(Md5.Hash(Encoding.ASCII.GetBytes(message))));

    /// <summary>Every length from 0 to 300 crosses the 55/56/64-byte padding boundaries several times.</summary>
#pragma warning disable CA5351 // MD5 is what is under test: the platform's is the oracle for the package's own.
    [Fact]
    public void Agrees_with_the_platform_on_generated_inputs() =>
        Gen.Byte.Array[0, 300].Sample(
            bytes => Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(bytes)), Convert.ToHexStringLower(Md5.Hash(bytes))),
            iter: 2_000);
#pragma warning restore CA5351
}
