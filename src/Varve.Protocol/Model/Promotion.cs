// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Log;

namespace Varve.Protocol.Model;

/// <summary>
/// What an operator asks when promoting a replica (ADR 0185 decision 4): the
/// source they say it is a copy of, and the position they mean to keep.
/// </summary>
/// <param name="Source">The source store's address, as the copy's lineage record names it.</param>
/// <param name="Position">The last position of the copy the operator expects.</param>
[DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
public sealed record PromotionAsk(string Source, Position Position);

/// <summary>Why a promotion was refused (ADR 0185 decision 4), or that it was not.</summary>
public enum PromotionRefusal : byte
{
    /// <summary>Promoted: this instance writes the dataset.</summary>
    None = 0,

    /// <summary>This instance is not one that would write the dataset: neither its placedOn nor one of its standbyOn.</summary>
    NotInWriterSet = 1,

    /// <summary>This instance does not follow the replica warm.</summary>
    Cold = 2,

    /// <summary>The dataset is not a replica: it has no lineage record, or one this build does not read, or it is not in an object store.</summary>
    NotAReplica = 3,

    /// <summary>The copy's lineage names another source than the one asked for.</summary>
    SourceMismatch = 4,

    /// <summary>The copy's verified tail is not at the position asked for.</summary>
    PositionMismatch = 5,

    /// <summary>Another promotion or another claim came first.</summary>
    EpochMoved = 6,
}

/// <summary>A promotion's answer (ADR 0185 decision 4).</summary>
/// <param name="Refusal">Why it was refused, or <see cref="PromotionRefusal.None"/>.</param>
/// <param name="Epoch">The writer record's epoch after it.</param>
/// <param name="Position">The copy's tail as this instance verified it: where it writes on from, or what did not match.</param>
public readonly record struct PromotionAnswer(PromotionRefusal Refusal, TakeoverEpoch Epoch, Position Position);
