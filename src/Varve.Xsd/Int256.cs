// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Xsd;

/// <summary>
/// The 256-bit intermediates <see cref="XsdDecimal"/>'s multiplication and
/// division need when a 128-bit product would overflow.
/// </summary>
/// <remarks>
/// A fixed-point multiply is <c>(a × b) / 10¹⁸</c> and a divide is
/// <c>(a × 10¹⁸) / b</c>; both products can need up to 256 bits even when the
/// quotient fits in 128. This is the schoolbook: four 64-bit limb products
/// for the multiply, and binary long division for the divide, over unsigned
/// magnitudes with the sign handled by the caller. It is the slow path; the
/// callers take a 128-bit shortcut whenever the product provably fits.
/// </remarks>
internal static class Int256
{
    /// <summary>The unsigned 256-bit product of two unsigned 128-bit values.</summary>
    internal static (UInt128 High, UInt128 Low) Multiply(UInt128 a, UInt128 b)
    {
        ulong a0 = (ulong)a;
        ulong a1 = (ulong)(a >> 64);
        ulong b0 = (ulong)b;
        ulong b1 = (ulong)(b >> 64);

        ulong p00Hi = Math.BigMul(a0, b0, out ulong p00Lo);
        ulong p01Hi = Math.BigMul(a0, b1, out ulong p01Lo);
        ulong p10Hi = Math.BigMul(a1, b0, out ulong p10Lo);
        ulong p11Hi = Math.BigMul(a1, b1, out ulong p11Lo);

        // Column 1: p00Hi + p01Lo + p10Lo, with carries into column 2.
        UInt128 column1 = (UInt128)p00Hi + p01Lo + p10Lo;
        ulong limb1 = (ulong)column1;
        UInt128 carry1 = column1 >> 64;

        // Column 2: p01Hi + p10Hi + p11Lo + carry.
        UInt128 column2 = (UInt128)p01Hi + p10Hi + p11Lo + carry1;
        ulong limb2 = (ulong)column2;
        UInt128 carry2 = column2 >> 64;

        ulong limb3 = (ulong)((UInt128)p11Hi + carry2);

        return (((UInt128)limb3 << 64) | limb2, ((UInt128)limb1 << 64) | p00Lo);
    }

    /// <summary>
    /// Divides a 256-bit unsigned value by a 128-bit unsigned divisor,
    /// truncating. False when the quotient does not fit in 128 bits.
    /// </summary>
    internal static bool TryDivide(UInt128 high, UInt128 low, UInt128 divisor, out UInt128 quotient)
    {
        quotient = 0;

        if (divisor == 0 || high >= divisor)
        {
            // A dividend of at least divisor × 2^128 gives a quotient of at least 2^128.
            return false;
        }

        if (high == 0)
        {
            quotient = low / divisor;
            return true;
        }

        // Binary long division over the low 128 bits, with the remainder seeded
        // from the high half, which is below the divisor and so fits.
        UInt128 remainder = high;
        UInt128 result = 0;

        for (int bit = 127; bit >= 0; bit--)
        {
            // remainder < divisor < 2^128, so remainder << 1 can carry out of
            // 128 bits; detect that carry before shifting.
            bool carried = (remainder >> 127) != 0;
            remainder = (remainder << 1) | ((low >> bit) & 1);

            if (carried || remainder >= divisor)
            {
                remainder -= divisor;
                result |= (UInt128)1 << bit;
            }
        }

        quotient = result;
        return true;
    }
}
