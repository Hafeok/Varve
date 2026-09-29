// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Varve.Rdf;
using Varve.Sparql.Algebra;

namespace Varve.Sparql.Evaluation.Model;

/// <summary>What the evaluator asks a <see cref="IServiceHandler"/>.</summary>
public sealed class ServiceRequest
{
    /// <summary>A request.</summary>
    public ServiceRequest(RdfTerm endpoint, Service pattern, IReadOnlyList<Variable> variables, IReadOnlyList<IReadOnlyList<RdfTerm?>> incoming)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(incoming);
        Endpoint = endpoint;
        Pattern = pattern;
        Variables = variables;
        Incoming = incoming;
    }

    /// <summary>The endpoint's IRI.</summary>
    public RdfTerm Endpoint { get; }

    /// <summary>The <c>SERVICE</c> node as written: its pattern and its <c>SILENT</c> flag.</summary>
    public Service Pattern { get; }

    /// <summary>The variables of the pattern, the columns of <see cref="Incoming"/>.</summary>
    public IReadOnlyList<Variable> Variables { get; }

    /// <summary>
    /// The solutions the answer will be joined with, as terms over
    /// <see cref="Variables"/>, null where unbound. A handler may use them to
    /// narrow what it asks (Federated Query §2.4) or ignore them: the evaluator
    /// joins either way.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<RdfTerm?>> Incoming { get; }
}
