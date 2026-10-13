// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Reflection;
using Varve.Delta;
using Xunit;

namespace Varve.Protocol.Client.Tests;

/// <summary>
/// A consumer of the API or the feed ships no store (ADR 0128,
/// <c>ClientShipsNoStore</c>): neither <c>Varve.Protocol.Client</c> nor
/// <c>Varve.Delta</c> reaches <c>Varve.Store</c>, directly or through
/// anything they reference, so a stray reference cannot come back.
/// </summary>
public class ClosureTests
{
    [Theory]
    [InlineData(typeof(SparqlHttpClient))]
    [InlineData(typeof(ChangeFeedReader))]
    public void The_closure_holds_no_store(Type root)
    {
        HashSet<string> closure = new(StringComparer.Ordinal);
        Queue<AssemblyName> pending = new([root.Assembly.GetName()]);

        while (pending.Count > 0)
        {
            AssemblyName name = pending.Dequeue();

            if (name.Name is not { } simple || !simple.StartsWith("Varve.", StringComparison.Ordinal) || !closure.Add(simple))
            {
                continue;
            }

            foreach (AssemblyName referenced in Assembly.Load(name).GetReferencedAssemblies())
            {
                pending.Enqueue(referenced);
            }
        }

        Assert.Contains(root.Assembly.GetName().Name!, closure);
        Assert.DoesNotContain(closure, name => name.StartsWith("Varve.Store", StringComparison.Ordinal));
        Assert.Contains("Varve.Rdf", closure);
        Assert.DoesNotContain("Varve.Protocol", closure);
    }
}
