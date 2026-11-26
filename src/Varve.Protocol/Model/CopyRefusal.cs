// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Protocol.Model;

/// <summary>
/// Why an apply that declares a dataset a copy is refused (ADR 0185 decision
/// 10): the answering instance can see that the dataset is written in this
/// fleet, and that its lineage names no such source.
/// </summary>
/// <param name="Dataset">The dataset declared a copy.</param>
/// <param name="Detail">What the instance saw, and what the operator does.</param>
public readonly record struct CopyRefusal(DatasetName Dataset, RefusalDetail Detail);

/// <summary>What a refusal says to the operator who reads it: what was seen, and what to do.</summary>
/// <param name="Value">The sentence, as the problem's <c>detail</c> carries it.</param>
public readonly record struct RefusalDetail(string Value);
