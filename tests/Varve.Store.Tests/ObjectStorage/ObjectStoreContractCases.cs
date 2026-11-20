// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Varve.Store.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// The object-store contract of ADR 0134, case by case: the cases themselves
/// are the shipped suite's (<see cref="ObjectStoreVerification"/>, ADR 0183),
/// so the tests against the in-memory store, Azurite, an S3-compatible server
/// and a real account run exactly what <c>varve storage verify</c> runs.
/// </summary>
/// <remarks>
/// Each case takes a store and a prefix of its own, so cases against a real
/// server never see each other's objects, and throws when the store breaks
/// the contract.
/// </remarks>
internal static class ObjectStoreContractCases
{
    /// <summary>
    /// The rounds of each race: eight on a pull request, and what
    /// <c>VARVE_VERIFY_ROUNDS</c> says on the nightly run (docs/testing.md §8).
    /// </summary>
    public static readonly int Rounds = int.TryParse(Environment.GetEnvironmentVariable("VARVE_VERIFY_ROUNDS"), out int n) && n > 0 ? n : 8;

    public static (string Name, Func<IObjectStore, string, CancellationToken, Task> Run)[] All() =>
    [
        .. ObjectStoreVerification.Cases().Select(c => (c.Name, (Func<IObjectStore, string, CancellationToken, Task>)((store, prefix, ct) => c.Run(store, prefix, Rounds, ct)))),
    ];
}
