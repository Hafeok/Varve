// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Writes testdata/formats/proof-reserved/proof/: objects under the reserved
// name proof/ that no build of storage format 1 knows (#340, ADR 0216 as
// amended 2026-11-18). They are shaped like what #292 proposes, a hash tile
// and a checkpoint with its signature, and beside them a version marker of a
// format nobody has defined, an empty object and a deep path, so a reader
// that looked inside would have something to trip on. Every byte is a
// function of the text below, so a run writes the same bytes each time.
//
//   dotnet run generate.cs -- <fixture directory>
//
// It references no Varve package: the point is bytes Varve does not write.

using System.Security.Cryptography;
using System.Text;

string root = Path.Combine(args.Length == 1 ? args[0] : throw new ArgumentException("usage: generate.cs <fixture directory>"), "proof");

void Write(string name, byte[] bytes)
{
    string path = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, bytes);
}

// A full tile of height 8: 256 hashes, each the SHA-256 of a label and its index.
byte[] tile = new byte[256 * 32];
for (int i = 0; i < 256; i++)
{
    SHA256.HashData(Encoding.UTF8.GetBytes("varve proof-reserved fixture, leaf " + i), tile.AsSpan(i * 32, 32));
}

Write("tile/8/0/000", tile);

// A checkpoint body at size 256, its root the hash of the tile, and a note signature line.
string rootHash = Convert.ToBase64String(SHA256.HashData(tile));
Write("checkpoints/00000000000000000256", Encoding.UTF8.GetBytes(
    "https://w3id.org/varve/6a0e7b3c-1d2f-4a5b-8c9d-0e1f2a3b4c5d\n256\n" + rootHash + "\n"));
Write("checkpoints/00000000000000000256.sig", Encoding.UTF8.GetBytes(
    "— fixture-witness " + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("not a signature"))) + "\n"));

// A version marker of a format no build defines.
Write("FORMAT", Encoding.UTF8.GetBytes("proof format 7, which no build of storage format 1 reads\n"));

// An empty object, and one at a deep path with every byte value.
Write("empty", []);
byte[] every = new byte[256];
for (int i = 0; i < every.Length; i++)
{
    every[i] = (byte)(255 - i);
}

Write("future/a/b/c/unknown.bin", every);
Console.WriteLine("wrote " + root);
