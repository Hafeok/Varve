// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Json.Canonical;
using Varve.Protocol.Endpoints;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Protocol;

/// <summary>
/// The row-level write rule a row-restricted write carries into the
/// sequencer (ADR 0236): its effective delta, judged against the true head,
/// whatever path composed it. A retraction of a quad the writer does not see
/// never reaches here from a path that judged the requested quads first, so
/// one that does is refused as any breach is.
/// </summary>
[Contract(typeof(TheRowLevelWriteRule.RequestedAndEffectiveBothChecked), Role = "the write rule under the sequencer")]
public sealed class RowWriteGuard : ICommitValidator
{
    private readonly IClassifier _classifier;
    private readonly RowScope _readable;
    private readonly RowScope _writable;

    /// <summary>The guard of a writer reading <paramref name="readable"/> and writing <paramref name="writable"/> rows, classified by <paramref name="classifier"/>.</summary>
    public RowWriteGuard(IClassifier classifier, RowScope readable, RowScope writable)
    {
        ArgumentNullException.ThrowIfNull(classifier);
        ArgumentNullException.ThrowIfNull(readable);
        ArgumentNullException.ThrowIfNull(writable);
        _classifier = classifier;
        _readable = readable;
        _writable = writable;
    }

    /// <inheritdoc />
    public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta)
    {
        ArgumentNullException.ThrowIfNull(proposed);

        if (_writable.IsAll || delta.IsEmpty)
        {
            return ValidationVerdict.Accept();
        }

        QuadOverlay before = new(proposed, delta.Inverse());
        RowWriteRule rule = new(before, _classifier.Classify(before, QuadDelta.Empty), proposed, _classifier.Classify(proposed, QuadDelta.Empty), _readable, _writable);

        try
        {
            foreach (Quad quad in delta.Retracted)
            {
                if (!rule.SeesBefore(in quad))
                {
                    throw new RowNotWritableException();
                }
            }

            rule.Check(delta.Asserted, delta.Retracted);
            return ValidationVerdict.Accept();
        }
        catch (RowNotWritableException)
        {
            return ValidationVerdict.Reject(Report());
        }
    }

    /// <summary>The one refusal, naming nothing (ADR 0236).</summary>
    internal static IReadOnlyList<RdfTerm> Report()
    {
        ArrayBufferWriter<byte> json = new();
        CanonicalJsonWriter writer = new(json);
        writer.StartObject();
        writer.WriteName("detail"u8);
        writer.WriteString("Nothing was committed; the request writes a row outside the writable subject set.");
        writer.EndObject();
        return ValidatorProblem.Report(ProblemType.RowNotWritable, json.WrittenSpan.ToArray());
    }
}
