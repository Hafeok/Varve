// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics.CodeAnalysis;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Protocol.Model;
using Varve.Store;

namespace Varve.Protocol;

/// <summary>
/// Which dataset a request names. The host's: it maps a name to an open
/// dataset, and the store never learns the name (ADR 0093).
/// </summary>
/// <remarks>
/// A host that serves one dataset mounts the endpoints without a
/// <c>{dataset}</c> route segment, and is asked for <see cref="DatasetName.Default"/>.
/// An unknown name is answered <see langword="false"/>, which the endpoints
/// turn into the same <c>404</c> as an invalid one.
/// </remarks>
[Contract(typeof(VarveProtocolAndVarveServer.DatasetResolverSeam), Role = "the host's map from a dataset name to an open dataset")]
public interface IDatasetResolver
{
    /// <summary>The open dataset the name maps to.</summary>
    bool TryResolve(DatasetName name, [NotNullWhen(true)] out Dataset? dataset);
}
