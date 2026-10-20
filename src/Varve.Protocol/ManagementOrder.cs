// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Json.Canonical;
using Varve.Management.Model;
using Varve.Protocol.Endpoints;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Protocol;

/// <summary>
/// The order of the management positions each dataset's log records (ADR
/// 0165): a commit authorised at a position below the highest its dataset
/// already records is refused with <c>management-behind</c>, by a validator
/// that runs under the sequencer, so no two commits race past it. The
/// watermark rises when a commit is validated, before it is durable: a commit
/// that then fails leaves it higher than the log, which refuses more, never
/// less. A host seeds it from the head commit's attachment when it opens the
/// dataset as its writer.
/// </summary>
[Contract(typeof(TheManagementPositionOnTheCommit.ManagementPositionsNeverDecrease), Role = "the per-dataset watermark of recorded management positions")]
public sealed class ManagementOrder
{
    private readonly ConcurrentDictionary<DatasetName, Watermark> marks = new();

    /// <summary>The datatype of the attachment a commit records its management position as.</summary>
    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public const string PositionDatatype = "https://w3id.org/varve/ns#managementPosition";

    /// <summary>The highest management position <paramref name="dataset"/> records, as this writer knows.</summary>
    public ManagementPosition Highest(DatasetName dataset) => new(Volatile.Read(ref Mark(dataset).Value));

    /// <summary>Raises the watermark of <paramref name="dataset"/> to <paramref name="position"/>: a position its log records.</summary>
    public void Observe(DatasetName dataset, ManagementPosition position) => Raise(Mark(dataset), position.Value);

    /// <summary>
    /// The management position among a commit's attachments, when it records
    /// one: the typed literal <c>"p"^^varve:managementPosition</c>.
    /// </summary>
    public static ManagementPosition? PositionOf(IReadOnlyList<RdfTerm> attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);

        foreach (RdfTerm term in attachments)
        {
            if (term.Kind == RdfTermKind.Literal
                && term.DatatypeIri.SequenceEqual(Encoding.UTF8.GetBytes(PositionDatatype))
                && long.TryParse(Encoding.UTF8.GetString(term.Lexical), NumberStyles.None, CultureInfo.InvariantCulture, out long value))
            {
                return new ManagementPosition(value);
            }
        }

        return null;
    }

    /// <summary>The validator a write to <paramref name="dataset"/>, authorised at <paramref name="authorised"/>, carries.</summary>
    public ICommitValidator Validator(DatasetName dataset, ManagementPosition authorised) => new Check(Mark(dataset), authorised.Value);

    private Watermark Mark(DatasetName dataset) => marks.GetOrAdd(dataset, static _ => new Watermark());

    private static void Raise(Watermark mark, long value)
    {
        long seen = Volatile.Read(ref mark.Value);

        while (value > seen)
        {
            long found = Interlocked.CompareExchange(ref mark.Value, value, seen);

            if (found == seen)
            {
                return;
            }

            seen = found;
        }
    }

    private sealed class Watermark
    {
        internal long Value;
    }

    private sealed class Check(Watermark mark, long authorised) : ICommitValidator
    {
        public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta)
        {
            long recorded = Volatile.Read(ref mark.Value);

            if (authorised < recorded)
            {
                ArrayBufferWriter<byte> json = new();
                CanonicalJsonWriter writer = new(json);
                writer.StartObject();
                writer.WriteName("appliedPosition"u8);
                writer.WriteNumber(authorised);
                writer.WriteName("detail"u8);
                writer.WriteString("The dataset records management position " + recorded.ToString(CultureInfo.InvariantCulture)
                    + "; this instance authorised the write at " + authorised.ToString(CultureInfo.InvariantCulture) + ".");
                writer.WriteName("position"u8);
                writer.WriteNumber(recorded);
                writer.EndObject();
                return ValidationVerdict.Reject(ValidatorProblem.Report(ProblemType.ManagementBehind, json.WrittenSpan.ToArray()));
            }

            Raise(mark, authorised);
            return ValidationVerdict.Accept();
        }
    }
}
