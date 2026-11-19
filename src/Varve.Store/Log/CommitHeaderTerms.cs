// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Rdf;

namespace Varve.Store.Log;

/// <summary>
/// A commit's header read without its delta (#310): what a reader of
/// metadata alone needs, such as a host's index of the attachments it
/// records (ADR 0246). Each term is resolved from the dataset's dictionary
/// when it is first asked for, so a reader pays only for what it reads.
/// </summary>
public sealed class CommitHeaderTerms
{
    private readonly Dataset _dataset;
    private readonly ulong _agent;
    private readonly ulong _cause;
    private readonly ulong _graphScope;
    private readonly ulong[] _attachments;
    private RdfTerm[]? _resolved;

    internal CommitHeaderTerms(Dataset dataset, Position position, CommitKind kind, CommitTimestamp timestamp, ulong agent, ulong cause, ulong graphScope, ulong[] attachments)
    {
        _dataset = dataset;
        Position = position;
        Kind = kind;
        Timestamp = timestamp;
        _agent = agent;
        _cause = cause;
        _graphScope = graphScope;
        _attachments = attachments;
    }

    /// <summary>Its position in the log.</summary>
    public Position Position { get; }

    /// <summary>What kind of commit it is.</summary>
    public CommitKind Kind { get; }

    /// <summary>When the sequencer closed it.</summary>
    public CommitTimestamp Timestamp { get; }

    /// <summary>Who made it; null when no agent was recorded.</summary>
    public RdfTerm? Agent => Resolve(_agent);

    /// <summary>Why; null when no cause was recorded.</summary>
    public RdfTerm? Cause => Resolve(_cause);

    /// <summary>The graph it declares it is about; null when none.</summary>
    public RdfTerm? GraphScope => Resolve(_graphScope);

    /// <summary>The terms the host and the validators attached, in the header's order.</summary>
    public IReadOnlyList<RdfTerm> Attachments
    {
        get
        {
            if (_resolved is { } resolved)
            {
                return resolved;
            }

            RdfTerm[] terms = new RdfTerm[_attachments.Length];

            for (int i = 0; i < terms.Length; i++)
            {
                terms[i] = _dataset.TermAtHead(_attachments[i]);
            }

            _resolved = terms;
            return terms;
        }
    }

    private RdfTerm? Resolve(ulong id) => id == 0 ? null : _dataset.TermAtHead(id);
}
