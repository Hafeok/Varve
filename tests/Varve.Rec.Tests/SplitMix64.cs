// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Rec.Tests;

/// <summary>
/// SplitMix64: a seeded generator of the generator's own, so that its
/// sequence is fixed by this file and never by a platform or a runtime
/// version. Integers only; nothing here touches floating point.
/// </summary>
internal sealed class SplitMix64(ulong seed)
{
    private ulong _state = seed;

    public ulong Next()
    {
        ulong z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>A number in [0, <paramref name="bound"/>), by rejection so it has no bias.</summary>
    public int Below(int bound)
    {
        ulong limit = ulong.MaxValue - (ulong.MaxValue % (ulong)bound);
        ulong value;

        do
        {
            value = Next();
        }
        while (value >= limit);

        return (int)(value % (ulong)bound);
    }

    /// <summary>True with probability <paramref name="percent"/> in 100.</summary>
    public bool Percent(int percent) => Below(100) < percent;

    public T Pick<T>(T[] choices) => choices[Below(choices.Length)];
}
