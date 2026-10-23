// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Management.Model;

namespace Varve.Management;

/// <summary>
/// The transitions of a dataset's event declarations (ADR 0167): from none to
/// <c>varve:Moving</c>, back to none (a cancel), or from <c>varve:Moving</c> to
/// <c>varve:Moved</c>, which is final. A dataset whose graph is removed is
/// deleted, not moved, and is not checked here.
/// </summary>
[Contract(typeof(EventDeclarationsMoveToTheManagementPlane.MoveTransitions), Role = "the allowed changes of a dataset's event declarations' state")]
public static class EventDeclarationMoves
{
    /// <summary>
    /// The findings for each dataset declared both <paramref name="before"/> and
    /// <paramref name="after"/> whose state changed in a way the move does not
    /// allow; none when every change is allowed.
    /// </summary>
    public static IReadOnlyList<DeclarationFinding> Check(Declarations before, Declarations after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        List<DeclarationFinding> findings = [];

        foreach ((string name, DatasetDeclaration now) in after.Datasets)
        {
            EventDeclarationState was = before.Datasets.TryGetValue(name, out DatasetDeclaration? then) ? then.EventDeclarations : EventDeclarationState.InDataset;

            if (!Allowed(was, now.EventDeclarations))
            {
                findings.Add(new DeclarationFinding(ManagementVocabulary.DatasetGraph(name), now.Node, ManagementVocabulary.EventDeclarations, Message(was, now.EventDeclarations)));
            }
        }

        return findings;
    }

    /// <summary>Whether the state may go from <paramref name="from"/> to <paramref name="to"/> in one commit.</summary>
    public static bool Allowed(EventDeclarationState from, EventDeclarationState to) => (from, to) switch
    {
        _ when from == to => true,
        (EventDeclarationState.InDataset, EventDeclarationState.Moving) => true,
        (EventDeclarationState.Moving, EventDeclarationState.InDataset) => true,
        (EventDeclarationState.Moving, EventDeclarationState.Moved) => true,
        _ => false,
    };

    private static string Message(EventDeclarationState from, EventDeclarationState to) => (from, to) switch
    {
        (EventDeclarationState.Moved, _) => "a dataset's event declarations, once moved, stay moved: varve:eventDeclarations varve:Moved is final (ADR 0167)",
        (EventDeclarationState.InDataset, EventDeclarationState.Moved) => "a dataset's event declarations are moved by POST /manage/datasets/{name}/events/move, which freezes them first: varve:Moved follows varve:Moving (ADR 0167)",
        _ => "this change of varve:eventDeclarations is not one the move allows (ADR 0167)",
    };
}
