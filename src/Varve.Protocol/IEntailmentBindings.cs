// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Protocol.Model;
using Varve.Sparql.Entailment;
using Varve.Sparql.Entailment.Model;

namespace Varve.Protocol;

/// <summary>
/// A dataset's TBox bindings as a query reads them (ADRs 0192, 0194): the
/// graphs each binding's TBox is merged into, and the regime a query gets
/// without <c>Varve-Entailment</c>. The protocol knows no declaration; the
/// host maps its bindings at the management position the read is served
/// under.
/// </summary>
[Contract(typeof(EntailmentRegimesOverHttp.DefaultFromTheBinding), Role = "the host's TBox bindings, per dataset")]
public interface IEntailmentBindings
{
    /// <summary>The bindings of <paramref name="dataset"/>; <see cref="EntailmentBindings.None"/> when it has none.</summary>
    EntailmentBindings Of(DatasetName dataset);
}

/// <summary>One dataset's TBox bindings and its default regime.</summary>
[Contract(typeof(EntailmentRegimesOverHttp.DefaultFromTheBinding), Role = "one dataset's TBox bindings")]
public sealed class EntailmentBindings
{
    /// <summary>The bindings <paramref name="covers"/>, with <paramref name="defaultRegime"/> the dataset's default.</summary>
    public EntailmentBindings(IReadOnlyList<TBoxCover> covers, EntailmentRegime defaultRegime)
    {
        ArgumentNullException.ThrowIfNull(covers);
        Covers = covers;
        Default = defaultRegime;
    }

    /// <summary>No binding: every graph's TBox is empty, and the default is simple entailment.</summary>
    public static EntailmentBindings None { get; } = new([], EntailmentRegime.Simple);

    /// <summary>The bindings, each its TBox graphs and the graphs it covers.</summary>
    public IReadOnlyList<TBoxCover> Covers { get; }

    /// <summary>The regime a query without <c>Varve-Entailment</c> is answered under.</summary>
    public EntailmentRegime Default { get; }
}
