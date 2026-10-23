// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Derivation.Model;

/// <summary>One dependency: <see cref="Source"/> depends on <see cref="Target"/>, through <see cref="Label"/>.</summary>
/// <typeparam name="TLabel">The caller's name for the relation, such as the parameter a shape refers through.</typeparam>
/// <param name="Source">The dependant vertex.</param>
/// <param name="Target">The vertex it depends on.</param>
/// <param name="Polarity">How the target's truth reaches the source.</param>
/// <param name="Label">The caller's label.</param>
[DesignDecision(typeof(VarveDerivation.DerivationAtLayerTwo), Scope = ExceptionScope.Boundary)]
public readonly record struct DependencyEdge<TLabel>(int Source, int Target, Polarity Polarity, TLabel Label);
