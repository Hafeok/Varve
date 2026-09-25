// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Sparql.Algebra;

/// <summary>A query variable, by name: <c>?x</c> and <c>$x</c> are the same variable.</summary>
public readonly record struct Variable
{
    /// <summary>A variable from its name, without the <c>?</c> or <c>$</c>.</summary>
    public Variable(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    /// <summary>The name, without the sigil.</summary>
    public string Name { get; }

    /// <summary>Renders as <c>?name</c>.</summary>
    public override string ToString() => "?" + Name;
}
