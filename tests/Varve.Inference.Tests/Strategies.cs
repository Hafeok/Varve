// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Benchmarks.Candidates;

namespace Varve.Inference.Tests;

/// <summary>The retraction strategies by name (ADR 0196): DRed and the baseline in the engine, the candidates not chosen with the benchmarks.</summary>
internal static class Strategies
{
    internal static Retraction Named(string name) => name switch
    {
        "DeleteAndRederive" => Retraction.DeleteAndRederive,
        "FromScratch" => Retraction.FromScratch,
        "RecursiveCounting" => RecursiveCounting.Instance,
        "BackwardForward" => BackwardForward.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such strategy."),
    };
}
