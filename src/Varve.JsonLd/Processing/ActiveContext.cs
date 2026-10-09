// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.JsonLd.Json;
using Varve.Rdf;

namespace Varve.JsonLd.Processing;

/// <summary>The container mapping of a term, as a set of keywords (JSON-LD 1.1 API §4.2.2 step 19).</summary>
[Flags]
internal enum Container
{
    None = 0,
    List = 1,
    Set = 2,
    Index = 4,
    Id = 8,
    Type = 16,
    Language = 32,
    Graph = 64,
}

/// <summary>Whether a term's language mapping is absent, explicitly null, or a tag.</summary>
internal enum Mapping : byte
{
    Absent,
    Null,
    Set,
}

/// <summary>
/// A term definition (JSON-LD 1.1 API §4.2). One object per term per context
/// met (ADR 0112, <c>ContextsAreNotPerQuad</c>); every string in it is a
/// range of the tree's text.
/// </summary>
[DesignDecision(typeof(JsonLdOverUtf8Json.ContextsAreNotPerQuad), Scope = ExceptionScope.HotPath)]
internal sealed class TermDefinition
{
    /// <summary>The IRI mapping; <see cref="TextRange.None"/> is the null mapping of a term defined with <c>"@id": null</c>.</summary>
    public TextRange Iri = TextRange.None;

    public bool Prefix;
    public bool Protected;
    public bool Reverse;
    public TextRange BaseUrl = TextRange.None;

    /// <summary>The type mapping: an IRI, or the text of <c>@id</c>, <c>@vocab</c>, <c>@json</c> or <c>@none</c>; None when absent.</summary>
    public TextRange Type = TextRange.None;

    public Mapping Language;
    public TextRange LanguageTag = TextRange.None;

    public Mapping Direction;
    public TextDirection DirectionValue;

    public Container Container;
    public TextRange Index = TextRange.None;

    /// <summary>The nest value as a name id, or -1.</summary>
    public int Nest = -1;

    /// <summary>The scoped context: a node of the tree, or -1.</summary>
    public int LocalContext = -1;

    public bool HasLocalContext;

    /// <summary>Whether two definitions are the same apart from <see cref="Protected"/> (§4.2.2 step 27.1).</summary>
    public bool SameAs(TermDefinition other, JsonTree tree)
    {
        return tree.TextEquals(Iri, other.Iri)
            && Prefix == other.Prefix
            && Reverse == other.Reverse
            && tree.TextEquals(Type, other.Type)
            && Language == other.Language
            && (Language != Mapping.Set || tree.TextEquals(LanguageTag, other.LanguageTag))
            && Direction == other.Direction
            && DirectionValue == other.DirectionValue
            && Container == other.Container
            && tree.TextEquals(Index, other.Index)
            && Nest == other.Nest
            && HasLocalContext == other.HasLocalContext
            && (!HasLocalContext || tree.SameValue(LocalContext, other.LocalContext))
            && tree.TextEquals(BaseUrl, other.BaseUrl);
    }
}

/// <summary>
/// An active context (JSON-LD 1.1 API §4.1): the term definitions, the base
/// IRI, the vocabulary mapping, the default language and direction, and the
/// previous context a non-propagating one remembers. Allocated once per
/// <c>@context</c> processed (ADR 0112, <c>ContextsAreNotPerQuad</c>).
/// </summary>
[DesignDecision(typeof(JsonLdOverUtf8Json.ContextsAreNotPerQuad), Scope = ExceptionScope.HotPath)]
internal sealed class ActiveContext
{
    public readonly Dictionary<int, TermDefinition> Terms = [];

    /// <summary>The base IRI; None is null.</summary>
    public TextRange Base = TextRange.None;

    /// <summary>The document's own base, which a <c>null</c> context resets to.</summary>
    public TextRange OriginalBase = TextRange.None;

    public TextRange Vocab = TextRange.None;
    public TextRange DefaultLanguage = TextRange.None;
    public TextDirection DefaultDirection;
    public ActiveContext? Previous;

    public ActiveContext Clone()
    {
        ActiveContext copy = new()
        {
            Base = Base,
            OriginalBase = OriginalBase,
            Vocab = Vocab,
            DefaultLanguage = DefaultLanguage,
            DefaultDirection = DefaultDirection,
            Previous = Previous,
        };

        foreach (KeyValuePair<int, TermDefinition> term in Terms)
        {
            copy.Terms.Add(term.Key, term.Value);
        }

        return copy;
    }

    public TermDefinition? Term(int nameId) => Terms.TryGetValue(nameId, out TermDefinition? definition) ? definition : null;

    public bool HasProtectedTerms()
    {
        foreach (TermDefinition definition in Terms.Values)
        {
            if (definition.Protected)
            {
                return true;
            }
        }

        return false;
    }
}
