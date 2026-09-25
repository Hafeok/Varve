// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers.Binary;

namespace Varve.Sparql.Evaluation.Expressions;

/// <summary>
/// MD5, RFC 1321, for SPARQL's <c>MD5()</c> (§17.4.6.1). The package's own
/// because the BCL's <c>MD5</c> throws <c>PlatformNotSupportedException</c> in
/// the browser, which constraint 3 puts this package in; the SHA family works
/// there and is the BCL's. Tested against RFC 1321 §A.5's vectors, and against
/// the BCL where it runs. Not for security: SPARQL asks for the function.
/// </summary>
internal static class Md5
{
    // RFC 1321 §3.4: T[i] = floor(2^32 × |sin(i + 1)|), and the per-round shifts.
    private static ReadOnlySpan<uint> Constants =>
    [
        0xd76aa478, 0xe8c7b756, 0x242070db, 0xc1bdceee, 0xf57c0faf, 0x4787c62a, 0xa8304613, 0xfd469501,
        0x698098d8, 0x8b44f7af, 0xffff5bb1, 0x895cd7be, 0x6b901122, 0xfd987193, 0xa679438e, 0x49b40821,
        0xf61e2562, 0xc040b340, 0x265e5a51, 0xe9b6c7aa, 0xd62f105d, 0x02441453, 0xd8a1e681, 0xe7d3fbc8,
        0x21e1cde6, 0xc33707d6, 0xf4d50d87, 0x455a14ed, 0xa9e3e905, 0xfcefa3f8, 0x676f02d9, 0x8d2a4c8a,
        0xfffa3942, 0x8771f681, 0x6d9d6122, 0xfde5380c, 0xa4beea44, 0x4bdecfa9, 0xf6bb4b60, 0xbebfbc70,
        0x289b7ec6, 0xeaa127fa, 0xd4ef3085, 0x04881d05, 0xd9d4d039, 0xe6db99e5, 0x1fa27cf8, 0xc4ac5665,
        0xf4292244, 0x432aff97, 0xab9423a7, 0xfc93a039, 0x655b59c3, 0x8f0ccc92, 0xffeff47d, 0x85845dd1,
        0x6fa87e4f, 0xfe2ce6e0, 0xa3014314, 0x4e0811a1, 0xf7537e82, 0xbd3af235, 0x2ad7d2bb, 0xeb86d391,
    ];

    private static ReadOnlySpan<byte> Shifts =>
    [
        7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
        5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
        4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
        6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21,
    ];

    internal static byte[] Hash(ReadOnlySpan<byte> message)
    {
        uint a0 = 0x67452301, b0 = 0xefcdab89, c0 = 0x98badcfe, d0 = 0x10325476;

        // §3.1–§3.2: pad with a one bit, zeros to 56 mod 64, then the bit length.
        int padded = ((message.Length + 8) / 64 + 1) * 64;
        byte[] buffer = new byte[padded];
        message.CopyTo(buffer);
        buffer[message.Length] = 0x80;
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(padded - 8), (ulong)message.Length * 8);

        Span<uint> m = stackalloc uint[16];
        for (int block = 0; block < padded; block += 64)
        {
            for (int i = 0; i < 16; i++)
            {
                m[i] = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(block + (i * 4)));
            }

            uint a = a0, b = b0, c = c0, d = d0;
            for (int i = 0; i < 64; i++)
            {
                uint f;
                int g;
                switch (i >> 4)
                {
                    case 0:
                        f = (b & c) | (~b & d);
                        g = i;
                        break;
                    case 1:
                        f = (d & b) | (~d & c);
                        g = ((5 * i) + 1) & 15;
                        break;
                    case 2:
                        f = b ^ c ^ d;
                        g = ((3 * i) + 5) & 15;
                        break;
                    default:
                        f = c ^ (b | ~d);
                        g = (7 * i) & 15;
                        break;
                }

                uint rotated = a + f + Constants[i] + m[g];
                a = d;
                d = c;
                c = b;
                b += (rotated << Shifts[i]) | (rotated >> (32 - Shifts[i]));
            }

            a0 += a;
            b0 += b;
            c0 += c;
            d0 += d;
        }

        byte[] digest = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(digest, a0);
        BinaryPrimitives.WriteUInt32LittleEndian(digest.AsSpan(4), b0);
        BinaryPrimitives.WriteUInt32LittleEndian(digest.AsSpan(8), c0);
        BinaryPrimitives.WriteUInt32LittleEndian(digest.AsSpan(12), d0);
        return digest;
    }
}
