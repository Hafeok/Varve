// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Xunit;

namespace Varve.Rec.Tests;

/// <summary>The REC generator's gates (ADR 0141).</summary>
public sealed class RecPortfolioTests
{
    /// <summary>The seed every stated dataset is generated with.</summary>
    public const ulong Seed = 20261015;

    private static string Vendored(string file) => Path.Combine(Root(), "testdata", "rec", "v4.1.0", file);

    private static string Root()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Varve.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("The repository root was not found above " + AppContext.BaseDirectory + ".");
    }

    [Fact]
    public void the_vendored_files_are_the_pinned_release()
    {
        foreach (string line in File.ReadAllLines(Vendored("SHA256SUMS")))
        {
            string[] parts = line.Split("  ");
            string actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Vendored(parts[1]))));
            Assert.True(parts[0] == actual, parts[1] + " is not the file vendored at v4.1.0");
        }
    }

    [Fact]
    public void every_class_and_property_generated_is_rec_s_or_brick_s()
    {
        string rec = File.ReadAllText(Vendored("rec.ttl"));
        string brick = File.ReadAllText(Vendored("Brick+patches.ttl"));
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (RecCommit commit in RecPortfolio.Generate(RecSize.S, Seed).Take(400))
        {
            foreach (RecQuad quad in commit.Asserted)
            {
                seen.Add(quad.Predicate);

                if (quad.Predicate == "<http://www.w3.org/1999/02/22-rdf-syntax-ns#type>")
                {
                    seen.Add(quad.Object);
                }
            }
        }

        foreach (string term in seen)
        {
            Match rec4 = Regex.Match(term, "^<https://w3id.org/rec#(.+)>$");
            Match brickTerm = Regex.Match(term, "^<https://brickschema.org/schema/Brick#(.+)>$");

            if (rec4.Success)
            {
                Assert.True(Regex.IsMatch(rec, "^rec:" + rec4.Groups[1].Value + "\\b|sh:path rec:" + rec4.Groups[1].Value + " ", RegexOptions.Multiline), term + " is not in REC 4");
            }
            else if (brickTerm.Success)
            {
                Assert.True(Regex.IsMatch(brick, "^brick:" + brickTerm.Groups[1].Value + "\\b", RegexOptions.Multiline), term + " is not in Brick");
            }
            else
            {
                Assert.True(term is "<http://www.w3.org/1999/02/22-rdf-syntax-ns#type>" or "<http://www.w3.org/2000/01/rdf-schema#label>", term + " is neither REC's, Brick's, rdf:type nor rdfs:label");
            }
        }
    }

    /// <summary>
    /// The same seed and size give the same bytes on every platform: the
    /// hashes are pinned, and CI runs this on linux-x64 and linux-arm64.
    /// </summary>
    [Theory]
    [InlineData(RecSize.S, "c04a57dce9928447819e211ce8ad2247a35b1aa432055926a7f51c20ec01fad8")]
    [InlineData(RecSize.M, "a6d28c61521255ae578585a104d7f961420522f1942b36fda94407c868e6ce59")]
    [InlineData(RecSize.L, "799c72181914ad6b65f597abc397810f4770de32bab63bea4c4a7dc324e8750b")]
    public void the_same_seed_and_size_give_the_same_bytes(RecSize size, string expected)
    {
        Stats stats = Measure(size, out string hash);
        TestContext.Current.TestOutputHelper?.WriteLine(size + ": " + stats + ", SHA-256 " + hash);
        Assert.Equal(expected, hash);
    }

    [Fact]
    public void the_size_has_the_quads_and_commits_it_states()
    {
        Stats stats = Measure(RecSize.S, out _);
        (_, int commits) = RecPortfolio.Shape(RecSize.S);
        Assert.Equal(commits, stats.Commits);
        Assert.InRange(stats.Head, 180_000, 230_000);
        Assert.InRange(stats.RetractedShareAfterBuild, 15, 30);
    }

    internal sealed record Stats(int Commits, long Head, long Asserted, long Retracted, long RetractedShareAfterBuild)
    {
        public override string ToString() =>
            Commits.ToString(CultureInfo.InvariantCulture) + " commits, " + Head.ToString(CultureInfo.InvariantCulture) + " quads at the head, "
            + Asserted.ToString(CultureInfo.InvariantCulture) + " asserted, " + Retracted.ToString(CultureInfo.InvariantCulture) + " retracted, "
            + RetractedShareAfterBuild.ToString(CultureInfo.InvariantCulture) + "% of changes after the build retractions";
    }

    internal static Stats Measure(RecSize size, out string hash)
    {
        using IncrementalHashStream sink = new();
        int commits = 0;
        long asserted = 0;
        long retracted = 0;
        long laterAsserted = 0;
        long laterRetracted = 0;
        List<RecCommit> batch = [];

        foreach (RecCommit commit in RecPortfolio.Generate(size, Seed))
        {
            commits++;
            asserted += commit.Asserted.Count;
            retracted += commit.Retracted.Count;

            if (commit.Kind != "build")
            {
                laterAsserted += commit.Asserted.Count;
                laterRetracted += commit.Retracted.Count;
            }

            batch.Add(commit);

            if (batch.Count == 256)
            {
                RecPortfolio.Write(batch, sink);
                batch.Clear();
            }
        }

        RecPortfolio.Write(batch, sink);
        hash = sink.Finish();
        long later = laterAsserted + laterRetracted;
        return new Stats(commits, asserted - retracted, asserted, retracted, later == 0 ? 0 : laterRetracted * 100 / later);
    }

    /// <summary>A stream that only hashes what is written to it.</summary>
    private sealed class IncrementalHashStream : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public string Finish() => Convert.ToHexStringLower(_hash.GetHashAndReset());

        public override void Write(byte[] buffer, int offset, int count) => _hash.AppendData(buffer, offset, count);

        public override void Write(ReadOnlySpan<byte> buffer) => _hash.AppendData(buffer);

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hash.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
