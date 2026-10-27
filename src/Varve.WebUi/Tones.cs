// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Varve.WebUi.Atoms;

namespace Varve.WebUi;

/// <summary>
/// The one mapping from an API state word or a problem type to a tone
/// (docs/ui/README.md, Colour; ADR 0206 point 5). Organisms and pages call
/// it; no atom maps, and no page repeats the table. A word or a type it does
/// not know is neutral, and the tests fail on any the server documents that
/// has no tone here.
/// </summary>
public static class Tones
{
    // The state words the API writes: dataset state and role (ADRs 0113,
    // 0118, 0163), readiness, the system role and the bootstrap state, the
    // event declarations' move (ADR 0167), and the Web UI's own "anonymous".
    private static readonly FrozenDictionary<string, Tone> States = new Dictionary<string, Tone>(StringComparer.Ordinal)
    {
        ["open"] = Tone.Ok,
        ["ready"] = Tone.Ok,
        ["writer"] = Tone.Ok,
        ["applied"] = Tone.Ok,
        ["recorded"] = Tone.Ok,
        ["rebuilding"] = Tone.Warn,
        ["behind"] = Tone.Warn,
        ["moving"] = Tone.Warn,
        ["failed"] = Tone.Critical,
        ["superseded"] = Tone.Critical,
        ["mismatch"] = Tone.Critical,
        ["not-ready"] = Tone.Critical,
        ["closed"] = Tone.Neutral,
        ["follower"] = Tone.Neutral,
        ["elsewhere"] = Tone.Neutral,
        ["absent"] = Tone.Neutral,
        ["anonymous"] = Tone.Neutral,
        ["in-dataset"] = Tone.Neutral,
        ["moved"] = Tone.Neutral,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // Every problem type by its name under https://w3id.org/varve/problems/
    // (ADR 0119): waiting is warn, because the same request succeeds later
    // without anyone acting; everything else is critical.
    private static readonly FrozenDictionary<string, Tone> Problems = new Dictionary<string, Tone>(StringComparer.Ordinal)
    {
        ["management-behind"] = Tone.Warn,
        ["dataset-behind"] = Tone.Warn,
        ["validation-behind"] = Tone.Warn,
        ["server-busy"] = Tone.Warn,
        ["too-many-requests"] = Tone.Warn,
        ["too-many-live-tails"] = Tone.Warn,
        ["shutting-down"] = Tone.Warn,
        ["reader-forgotten"] = Tone.Warn,
        ["not-ready"] = Tone.Critical,
        ["sparql-syntax"] = Tone.Critical,
        ["rdf-syntax"] = Tone.Critical,
        ["bad-request"] = Tone.Critical,
        ["operation-failed"] = Tone.Critical,
        ["unauthorized"] = Tone.Critical,
        ["forbidden"] = Tone.Critical,
        ["agent-required"] = Tone.Critical,
        ["graph-not-writable"] = Tone.Critical,
        ["dataset-not-found"] = Tone.Critical,
        ["not-found"] = Tone.Critical,
        ["graph-not-found"] = Tone.Critical,
        ["position-not-reached"] = Tone.Critical,
        ["before-first-commit"] = Tone.Critical,
        ["below-archive-horizon"] = Tone.Critical,
        ["method-not-allowed"] = Tone.Critical,
        ["not-acceptable"] = Tone.Critical,
        ["conflict"] = Tone.Critical,
        ["dataset-open"] = Tone.Critical,
        ["event-catalogue-moved"] = Tone.Critical,
        ["precondition-failed"] = Tone.Critical,
        ["request-too-large"] = Tone.Critical,
        ["unsupported-media-type"] = Tone.Critical,
        ["rejected"] = Tone.Critical,
        ["event-declaration-invalid"] = Tone.Critical,
        ["event-schema-incompatible"] = Tone.Critical,
        ["event-not-rendered"] = Tone.Critical,
        ["shapes-violated"] = Tone.Critical,
        ["binding-not-incremental"] = Tone.Critical,
        ["shapes-invalid"] = Tone.Critical,
        ["memory-limit-exceeded"] = Tone.Critical,
        ["as-of-distance-exceeded"] = Tone.Critical,
        ["unavailable"] = Tone.Critical,
        ["read-limit-exceeded"] = Tone.Critical,
        ["writer-superseded"] = Tone.Critical,
        ["writer-moved"] = Tone.Critical,
        ["declaration-invalid"] = Tone.Critical,
        ["read-unannounced-extent-gone"] = Tone.Critical,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The tone of an API state word, neutral when the word is not one the mapping knows.</summary>
    public static Tone OfState(string word) => TryOfState(word, out Tone tone) ? tone : Tone.Neutral;

    /// <summary>Whether the mapping knows <paramref name="word"/>, and its tone.</summary>
    public static bool TryOfState(string word, out Tone tone) => States.TryGetValue(word, out tone);

    /// <summary>The tone of a problem type by its name, critical when the mapping does not know it.</summary>
    public static Tone OfProblem(string name) => TryOfProblem(name, out Tone tone) ? tone : Tone.Critical;

    /// <summary>Whether the mapping knows the problem type <paramref name="name"/>, and its tone.</summary>
    public static bool TryOfProblem(string name, out Tone tone) => Problems.TryGetValue(name, out tone);
}
