// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Management;
using Varve.Management.Model;
using Varve.Protocol;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Sparql.Entailment;
using Varve.Sparql.Entailment.Model;

namespace Varve.Server;

/// <summary>
/// The fleet's TBox bindings as a query reads them (ADR 0194 point 7): each
/// declared binding at the management position applied, its data graphs
/// never a <c>varve:</c> graph, and the dataset's default regime. Mapped once
/// per position the grant table is compiled at.
/// </summary>
internal sealed class EntailmentBindingsOfTheFleet(Fleet fleet) : IEntailmentBindings
{
    // Every graph the server owns: report, catalogue and management graphs (ADR 0194 point 2).
    private static readonly GraphScope Reserved = GraphScope.Of([], [ManagementVocabulary.Namespace], DefaultGraphAccess.Excluded);

    // The last table mapped; requests race to replace it, each with a whole mapping.
    private Mapped? _mapped;

    public EntailmentBindings Of(DatasetName dataset)
    {
        GrantTable table = fleet.Table;
        Mapped? held = System.Threading.Volatile.Read(ref _mapped);
        if (held is null || !ReferenceEquals(held.Table, table))
        {
            held = Map(table);
            System.Threading.Volatile.Write(ref _mapped, held);
        }

        return held.Bindings.TryGetValue(dataset.Value, out EntailmentBindings? bindings) ? bindings : EntailmentBindings.None;
    }

    private static Mapped Map(GrantTable table)
    {
        Dictionary<string, EntailmentBindings> map = new(System.StringComparer.Ordinal);
        foreach ((string name, DatasetDeclaration declared) in table.Declarations.Datasets)
        {
            if (declared.TBoxBindings.Count == 0)
            {
                continue;
            }

            List<TBoxCover> covers = [];
            foreach (TBoxBindingDeclaration binding in declared.TBoxBindings)
            {
                covers.Add(new TBoxCover(binding.TBoxGraphs, ShapesBindings.Scope(binding.DataGraphs)) { Excluded = Reserved });
            }

            EntailmentRegime regime = declared.DefaultEntailment is { } iri && EntailmentRegimes.TryFromIri(iri, out EntailmentRegime? stated) ? stated.Value : EntailmentRegime.Simple;
            map[name] = new EntailmentBindings(covers, regime);
        }

        return new Mapped(table, map);
    }

    private sealed record Mapped(GrantTable Table, Dictionary<string, EntailmentBindings> Bindings);
}
