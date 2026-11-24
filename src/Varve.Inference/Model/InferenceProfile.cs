// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Inference.Model;

/// <summary>A materialising profile (ADR 0197 point 1).</summary>
public enum InferenceProfile
{
    /// <summary><c>varve:RDFS</c>: the RDFS entailment rules, bounded.</summary>
    Rdfs = 1,

    /// <summary><c>varve:OWL2RL</c>: OWL 2 RL/RDF over the RDFS rules, with <c>sameAs</c> off.</summary>
    Owl2Rl = 2,
}
