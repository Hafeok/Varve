// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Events.Model;

/// <summary>
/// One event type as its catalogue graph declares it (docs/spec/events.md
/// §3.2): a well-formed declaration, read by <see cref="EventDeclarations"/>.
/// </summary>
public sealed class EventTypeDeclaration
{
    internal EventTypeDeclaration(
        RdfTerm node, RdfTerm? sourceGraph, string name, RdfTerm @class, int version, string select,
        string? subject, string? description, bool retired, IReadOnlyList<EventFieldDeclaration> fields, CompiledSelect compiled)
    {
        Node = node;
        SourceGraph = sourceGraph;
        Name = name;
        Class = @class;
        Version = version;
        Select = select;
        Subject = subject;
        Description = description;
        Retired = retired;
        Fields = fields;
        Compiled = compiled;
    }

    /// <summary>The type node: the declaration's identity.</summary>
    public RdfTerm Node { get; }

    /// <summary>The source graph whose catalogue declares it; <see langword="null"/> for the default graph.</summary>
    public RdfTerm? SourceGraph { get; }

    /// <summary>The short name, the CloudEvents <c>type</c>.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public string Name { get; }

    /// <summary>The class whose assertion is the event.</summary>
    public RdfTerm Class { get; }

    /// <summary>The declared version, at least 1.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public int Version { get; }

    /// <summary>The <c>SELECT</c> query's text.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public string Select { get; }

    /// <summary>The variable whose value is the CloudEvents <c>subject</c>, when one is declared.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public string? Subject { get; }

    /// <summary>The type's <c>rdfs:comment</c>.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public string? Description { get; }

    /// <summary>Whether the type is retired, and so renders no new event.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public bool Retired { get; }

    /// <summary>The fields, in field-name order.</summary>
    public IReadOnlyList<EventFieldDeclaration> Fields { get; }

    internal CompiledSelect Compiled { get; }
}

/// <summary>One field of an event type (docs/spec/events.md §3.2, §5.2).</summary>
public sealed class EventFieldDeclaration
{
    internal EventFieldDeclaration(RdfTerm node, string name, RdfTerm datatype, bool required, bool asString, bool keepLanguage, string? description)
    {
        Node = node;
        Name = name;
        Datatype = datatype;
        Required = required;
        AsString = asString;
        KeepLanguage = keepLanguage;
        Description = description;
    }

    /// <summary>The field node.</summary>
    public RdfTerm Node { get; }

    /// <summary>The <c>data</c> member's name, and the projected variable's.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public string Name { get; }

    /// <summary>The declared datatype's IRI.</summary>
    public RdfTerm Datatype { get; }

    /// <summary>Whether every event carries the field.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public bool Required { get; }

    /// <summary>Whether the member is always a JSON string (<c>varve:fieldAs "string"</c>).</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public bool AsString { get; }

    /// <summary>Whether a language-tagged value renders as an object that keeps its language and direction.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public bool KeepLanguage { get; }

    /// <summary>The field's <c>rdfs:comment</c>.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public string? Description { get; }
}
