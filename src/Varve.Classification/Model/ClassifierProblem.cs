// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Classification.Model;

/// <summary>Why a binding's shapes graph does not classify (ADR 0232): a refused annotation, named for the operator.</summary>
/// <param name="Message">What is refused, naming the shape.</param>
public readonly record struct ClassifierProblem(string Message);
