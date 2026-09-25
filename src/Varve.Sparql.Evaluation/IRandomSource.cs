// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Sparql.Evaluation;

/// <summary>
/// Randomness for <c>RAND()</c>, <c>UUID()</c> and <c>STRUUID()</c>, supplied by
/// the caller (ADR 0056). The evaluator never chooses a generator: over the
/// BCL's, the adapter is one line —
/// <c>RandomNumberGenerator.Fill(destination)</c> in <see cref="NextBytes"/>.
/// </summary>
public interface IRandomSource
{
    /// <summary>Fills <paramref name="destination"/> with random bytes.</summary>
    void NextBytes(Span<byte> destination);
}
