// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Linked into every packable project by Directory.Build.targets. See ADR 0026.
//
// It is internal, which does three things at once: it never enters a public API
// baseline, it never reaches a consumer, and it makes the per-assembly
// duplication harmless because two internal types cannot collide.
//
// VARVE0006 is reserved and not implemented. It will match this attribute by
// full name — Varve.HotPathAttribute — rather than by symbol identity, because
// there is no single symbol to identify. Marking starts before the rule exists
// on purpose: deciding which members are hot paths is a judgement made while
// writing them.

using System;

namespace Varve;

/// <summary>
/// Marks a member that must not box, capture closures, allocate arrays or
/// strings, use LINQ, or call members that are not hot-path-safe.
/// </summary>
/// <remarks>
/// Inert until VARVE0006 exists. See <c>docs/adr/0026-hotpath-attribute.md</c>.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor,
    Inherited = false)]
internal sealed class HotPathAttribute : Attribute
{
}
