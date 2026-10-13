// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;

// The orders API of a shop, calling Varve as its signed-in user (ADR 0127).
namespace Varve.OnBehalfOf.Sample;

internal static class Program
{
    private static Task Main(string[] args) => OrdersApi.Build(args).RunAsync();
}
