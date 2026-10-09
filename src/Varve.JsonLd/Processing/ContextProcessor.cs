// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Iri;
using Varve.JsonLd.Json;
using Varve.JsonLd.Model;
using Varve.Rdf;

namespace Varve.JsonLd.Processing;

/// <summary>
/// Context processing (JSON-LD 1.1 API §4.1.2), create term definition
/// (§4.2.2) and IRI expansion (§5.2.2), over the tree. Every allocation here
/// is per context met, never per quad (ADR 0112,
/// <c>ContextsAreNotPerQuad</c>).
/// </summary>
[DesignDecision(typeof(JsonLdOverUtf8Json.ContextsAreNotPerQuad), Scope = ExceptionScope.HotPath)]
internal sealed class ContextProcessor
{
    /// <summary>The limit on remote contexts one document may pull in (§4.1.2 step 5.2.3, "context overflow").</summary>
    private const int MaxRemoteContexts = 32;

    private readonly JsonTree _tree;
    private readonly NameTable _names;
    private readonly JsonLdDocumentLoader? _loader;
    private readonly Dictionary<string, int> _remote = new(StringComparer.Ordinal);
    private readonly TextRange[] _keywords = new TextRange[Keyword.Count];

    internal ContextProcessor(JsonTree tree, JsonLdDocumentLoader? loader)
    {
        _tree = tree;
        _names = tree.Names;
        _loader = loader;

        for (int i = 0; i < Keyword.Count; i++)
        {
            _keywords[i] = tree.AddText(_names.Bytes(i));
        }
    }

    /// <summary>The text of a keyword, as a range.</summary>
    internal TextRange KeywordText(int id) => _keywords[id];

    /// <summary>The keyword a range spells, or -1.</summary>
    internal int KeywordOf(TextRange range)
    {
        if (range.IsNone)
        {
            return -1;
        }

        int id = _names.Find(_tree.Bytes(range));
        return NameTable.IsKeyword(id) ? id : -1;
    }

    internal bool IsKeyword(TextRange range) => KeywordOf(range) >= 0;

    internal bool Is(TextRange range, int keyword) => KeywordOf(range) == keyword;

    // ------------------------------------------------- context processing

    /// <summary>§4.1.2. <paramref name="localContext"/> is a node of the tree; the result is a new context or the same one.</summary>
    internal ActiveContext Process(
        ActiveContext active,
        int localContext,
        TextRange baseUrl,
        List<TextRange>? remoteContexts = null,
        bool overrideProtected = false,
        bool propagate = true,
        bool validateScopedContext = true)
    {
        ActiveContext result = active.Clone();
        remoteContexts ??= [];

        // 2. @propagate on a context definition.
        if (_tree.IsObject(localContext))
        {
            int propagateValue = _tree.Member(localContext, Keyword.Propagate);

            if (propagateValue >= 0)
            {
                if (!_tree.IsBoolean(propagateValue))
                {
                    throw new JsonLdException(JsonLdErrorCode.InvalidPropagateValue, "@propagate must be true or false.");
                }

                propagate = _tree.Kind(propagateValue) == JsonKind.True;
            }
        }

        // 3.
        if (!propagate && result.Previous is null)
        {
            result.Previous = active;
        }

        // 4. and 5.
        int first = _tree.IsArray(localContext) ? _tree.First(localContext) : localContext;
        bool single = !_tree.IsArray(localContext);

        for (int context = first; context >= 0; context = single ? -1 : _tree.Next(context))
        {
            // 5.1 null resets.
            if (_tree.IsNull(context))
            {
                if (!overrideProtected && result.HasProtectedTerms())
                {
                    throw new JsonLdException(JsonLdErrorCode.InvalidContextNullification, "A null context would drop protected terms.");
                }

                ActiveContext fresh = new()
                {
                    Base = active.OriginalBase,
                    OriginalBase = active.OriginalBase,
                    Previous = propagate ? null : result.Previous,
                };
                result = fresh;
                continue;
            }

            // 5.2 a remote context.
            if (_tree.IsString(context))
            {
                TextRange iri = ResolveOrKeep(baseUrl, _tree.Bytes(context));

                if (!validateScopedContext && Contains(remoteContexts, iri))
                {
                    continue;
                }

                if (remoteContexts.Count >= MaxRemoteContexts)
                {
                    throw new JsonLdException(JsonLdErrorCode.ContextOverflow, "More than " + MaxRemoteContexts.ToString(System.Globalization.CultureInfo.InvariantCulture) + " remote contexts.");
                }

                if (Contains(remoteContexts, iri))
                {
                    throw new JsonLdException(JsonLdErrorCode.RecursiveContextInclusion, "The context " + Text(iri) + " includes itself.");
                }

                remoteContexts.Add(iri);
                int loaded = LoadContext(iri, out TextRange documentIri);
                result = Process(result, loaded, documentIri, [.. remoteContexts], overrideProtected: false, propagate: true, validateScopedContext);
                continue;
            }

            // 5.3
            if (!_tree.IsObject(context))
            {
                throw new JsonLdException(JsonLdErrorCode.InvalidLocalContext, "A context is an object, a string, null or an array of those.");
            }

            int definition = context;

            // 5.5 @version
            int version = _tree.Member(definition, Keyword.Version);

            if (version >= 0 && !(_tree.IsNumber(version) && _tree.Bytes(version).SequenceEqual("1.1"u8)))
            {
                throw new JsonLdException(JsonLdErrorCode.InvalidVersionValue, "@version must be the number 1.1.");
            }

            // 5.6 @import
            int import = _tree.Member(definition, Keyword.Import);

            if (import >= 0)
            {
                if (!_tree.IsString(import))
                {
                    throw new JsonLdException(JsonLdErrorCode.InvalidImportValue, "@import must be a string.");
                }

                TextRange importIri = ResolveOrKeep(baseUrl, _tree.Bytes(import));
                int imported = LoadContext(importIri, out _);

                if (!_tree.IsObject(imported))
                {
                    throw new JsonLdException(JsonLdErrorCode.InvalidRemoteContext, "An imported context must be a context definition.");
                }

                if (_tree.HasMember(imported, Keyword.Import))
                {
                    throw new JsonLdException(JsonLdErrorCode.InvalidContextEntry, "An imported context may not itself have @import.");
                }

                // 5.6.8 merge, the importing context winning.
                int merged = _tree.AddObject();

                for (int member = _tree.First(imported); member >= 0; member = _tree.Next(member))
                {
                    _tree.AddMember(merged, _tree.NameOf(member), _tree.ValueOf(member));
                }

                for (int member = _tree.First(definition); member >= 0; member = _tree.Next(member))
                {
                    _tree.SetMember(merged, _tree.NameOf(member), _tree.ValueOf(member));
                }

                definition = merged;
            }

            // 5.7 @base
            int baseValue = _tree.Member(definition, Keyword.Base);

            if (baseValue >= 0 && remoteContexts.Count == 0)
            {
                if (_tree.IsNull(baseValue))
                {
                    result.Base = TextRange.None;
                }
                else if (_tree.IsString(baseValue) && IriRef.IsAbsolute(_tree.Bytes(baseValue)))
                {
                    result.Base = _tree.Range(baseValue);
                }
                else if (_tree.IsString(baseValue) && !result.Base.IsNone)
                {
                    result.Base = Resolve(result.Base, _tree.Bytes(baseValue));
                }
                else
                {
                    throw new JsonLdException(JsonLdErrorCode.InvalidBaseIri, "@base must be an IRI, or a relative reference with a base to resolve against.");
                }
            }

            // 5.8 @vocab
            int vocab = _tree.Member(definition, Keyword.Vocab);

            if (vocab >= 0)
            {
                if (_tree.IsNull(vocab))
                {
                    result.Vocab = TextRange.None;
                }
                else if (_tree.IsString(vocab))
                {
                    TextRange expanded = ExpandIri(result, _tree.Bytes(vocab), documentRelative: true, vocab: true, -1, null);

                    if (expanded.IsNone || !(IriRef.IsAbsolute(_tree.Bytes(expanded)) || IsBlankNode(_tree.Bytes(expanded))))
                    {
                        throw new JsonLdException(JsonLdErrorCode.InvalidVocabMapping, "@vocab must be an IRI or a blank node identifier.");
                    }

                    result.Vocab = expanded;
                }
                else
                {
                    throw new JsonLdException(JsonLdErrorCode.InvalidVocabMapping, "@vocab must be an IRI, a blank node identifier or null.");
                }
            }

            // 5.9 @language
            int language = _tree.Member(definition, Keyword.Language);

            if (language >= 0)
            {
                if (_tree.IsNull(language))
                {
                    result.DefaultLanguage = TextRange.None;
                }
                else if (_tree.IsString(language))
                {
                    result.DefaultLanguage = Lowercase(_tree.Bytes(language));
                }
                else
                {
                    throw new JsonLdException(JsonLdErrorCode.InvalidDefaultLanguage, "@language must be a string or null.");
                }
            }

            // 5.10 @direction
            int direction = _tree.Member(definition, Keyword.Direction);

            if (direction >= 0)
            {
                result.DefaultDirection = DirectionOf(direction);
            }

            // 5.11 @propagate was taken above; here it only needs to be boolean, which it was.

            // 5.12 and 5.13: the terms.
            Dictionary<int, bool> defined = [];
            bool protectedDefault = false;
            int protectedValue = _tree.Member(definition, Keyword.Protected);

            if (protectedValue >= 0)
            {
                if (!_tree.IsBoolean(protectedValue))
                {
                    throw new JsonLdException(JsonLdErrorCode.InvalidProtectedValue, "@protected must be true or false.");
                }

                protectedDefault = _tree.Kind(protectedValue) == JsonKind.True;
            }

            for (int member = _tree.First(definition); member >= 0; member = _tree.Next(member))
            {
                int key = _tree.NameOf(member);

                if (key is Keyword.Base or Keyword.Direction or Keyword.Import or Keyword.Language or Keyword.Propagate or Keyword.Protected or Keyword.Version or Keyword.Vocab)
                {
                    continue;
                }

                CreateTermDefinition(result, definition, key, defined, baseUrl, protectedDefault, overrideProtected, remoteContexts);
            }
        }

        return result;
    }

    private bool Contains(List<TextRange> ranges, TextRange iri)
    {
        foreach (TextRange range in ranges)
        {
            if (_tree.TextEquals(range, iri))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Dereferences a remote context through the caller's loader (ADR 0112, <c>RemoteContextsNeedALoader</c>); returns the <c>@context</c> value node.</summary>
    [DesignDecision(typeof(JsonLdOverUtf8Json.RemoteContextsNeedALoader), Scope = ExceptionScope.Boundary)]
    private int LoadContext(TextRange iri, out TextRange documentIri)
    {
        string key = Text(iri);
        documentIri = iri;

        if (_remote.TryGetValue(key, out int cached))
        {
            return cached;
        }

        if (_loader is null || !_loader(_tree.Bytes(iri), out ReadOnlyMemory<byte> document))
        {
            throw new JsonLdException(JsonLdErrorCode.LoadingRemoteContextFailed, "The remote context " + key + " could not be loaded" + (_loader is null ? "; no document loader is configured." : "."));
        }

        int root;

        try
        {
            root = JsonTreeReader.Read(document.Span, _tree, JsonLdErrorCode.LoadingRemoteContextFailed);
        }
        catch (JsonLdException exception) when (exception.Code == JsonLdErrorCode.LoadingRemoteContextFailed)
        {
            throw new JsonLdException(JsonLdErrorCode.LoadingRemoteContextFailed, "The remote context " + key + " is not JSON: " + exception.Message);
        }

        if (!_tree.IsObject(root) || !_tree.HasMember(root, Keyword.Context))
        {
            throw new JsonLdException(JsonLdErrorCode.InvalidRemoteContext, "The remote context " + key + " has no @context.");
        }

        int context = _tree.Member(root, Keyword.Context);
        _remote[key] = context;
        return context;
    }

    // ---------------------------------------------- create term definition

    /// <summary>§4.2.2.</summary>
    internal void CreateTermDefinition(
        ActiveContext active,
        int localContext,
        int term,
        Dictionary<int, bool> defined,
        TextRange baseUrl,
        bool protectedDefault,
        bool overrideProtected,
        List<TextRange> remoteContexts)
    {
        // 1.
        if (defined.TryGetValue(term, out bool done))
        {
            if (done)
            {
                return;
            }

            throw new JsonLdException(JsonLdErrorCode.CyclicIriMapping, "The term " + _names.Text(term) + " depends on itself.");
        }

        ReadOnlySpan<byte> termBytes = _names.Bytes(term);

        // 2.
        if (termBytes.Length == 0)
        {
            throw new JsonLdException(JsonLdErrorCode.InvalidTermDefinition, "A term may not be the empty string.");
        }

        defined[term] = false;

        // 3.
        int value = _tree.Member(localContext, term);

        // 4. @type may be redefined only to add @container: @set or @protected.
        if (term == Keyword.Type)
        {
            if (!_tree.IsObject(value) || _tree.Count(value) == 0)
            {
                throw new JsonLdException(JsonLdErrorCode.KeywordRedefinition, "@type may only be redefined with @container: @set or @protected.");
            }

            for (int member = _tree.First(value); member >= 0; member = _tree.Next(member))
            {
                int key = _tree.NameOf(member);
                int memberValue = _tree.ValueOf(member);

                if (key == Keyword.Container && _tree.StringEquals(memberValue, "@set"u8))
                {
                    continue;
                }

                if (key == Keyword.Protected && _tree.IsBoolean(memberValue))
                {
                    continue;
                }

                throw new JsonLdException(JsonLdErrorCode.KeywordRedefinition, "@type may only be redefined with @container: @set or @protected.");
            }
        }
        else if (NameTable.IsKeyword(term))
        {
            // 5.
            throw new JsonLdException(JsonLdErrorCode.KeywordRedefinition, "The keyword " + _names.Text(term) + " cannot be redefined.");
        }
        else if (NameTable.LooksLikeKeyword(termBytes))
        {
            // 5. a term shaped like a keyword is ignored, with a warning.
            defined[term] = true;
            return;
        }

        // 6.
        active.Terms.TryGetValue(term, out TermDefinition? previous);
        active.Terms.Remove(term);

        // 7.–9.
        bool simpleTerm;
        int idValue;

        if (value < 0 || _tree.IsNull(value))
        {
            simpleTerm = false;
            idValue = -2; // an explicit null @id
            value = -1;
        }
        else if (_tree.IsString(value))
        {
            simpleTerm = true;
            idValue = value;
            value = -1;
        }
        else if (_tree.IsObject(value))
        {
            simpleTerm = false;
            idValue = _tree.Member(value, Keyword.Id);

            if (idValue >= 0 && _tree.IsNull(idValue))
            {
                idValue = -2;
            }
        }
        else
        {
            throw new JsonLdException(JsonLdErrorCode.InvalidTermDefinition, "The definition of " + _names.Text(term) + " must be a string, an object or null.");
        }

        // 10.
        TermDefinition definition = new() { Protected = protectedDefault };

        // 11.
        if (value >= 0)
        {
            int protectedValue = _tree.Member(value, Keyword.Protected);

            if (protectedValue >= 0)
            {
                if (!_tree.IsBoolean(protectedValue))
                {
                    throw new JsonLdException(JsonLdErrorCode.InvalidProtectedValue, "@protected must be true or false.");
                }

                definition.Protected = _tree.Kind(protectedValue) == JsonKind.True;
            }
        }

        // 12. @type
        if (value >= 0)
        {
            int type = _tree.Member(value, Keyword.Type);

            if (type >= 0)
            {
                if (!_tree.IsString(type))
                {
                    throw new JsonLdException(JsonLdErrorCode.InvalidTypeMapping, "The @type of " + _names.Text(term) + " must be a string.");
                }

                TextRange expandedType = ExpandIri(active, _tree.Bytes(type), documentRelative: false, vocab: true, localContext, defined);
                int keyword = KeywordOf(expandedType);

                if (!(keyword is Keyword.Id or Keyword.Vocab or Keyword.Json or Keyword.None) && (expandedType.IsNone || !IriRef.IsAbsolute(_tree.Bytes(expandedType))))
                {
                    throw new JsonLdException(JsonLdErrorCode.InvalidTypeMapping, "The @type of " + _names.Text(term) + " must be an IRI, @id, @vocab, @json or @none.");
                }

                definition.Type = expandedType;
            }
        }

        // 13. @reverse
        if (value >= 0 && _tree.HasMember(value, Keyword.Reverse))
        {
            if (_tree.HasMember(value, Keyword.Id) || _tree.HasMember(value, Keyword.Nest))
            {
                throw new JsonLdException(JsonLdErrorCode.InvalidReverseProperty, "A reverse property takes no @id or @nest.");
            }

            int reverse = _tree.Member(value, Keyword.Reverse);

            if (!_tree.IsString(reverse))
            {
                throw new JsonLdException(JsonLdErrorCode.InvalidIriMapping, "@reverse must be a string.");
            }

            if (NameTable.LooksLikeKeyword(_tree.Bytes(reverse)))
            {
                defined[term] = true;
                return;
            }

            TextRange reverseIri = ExpandIri(active, _tree.Bytes(reverse), documentRelative: false, vocab: true, localContext, defined);

            if (reverseIri.IsNone || !(IriRef.IsAbsolute(_tree.Bytes(reverseIri)) || IsBlankNode(_tree.Bytes(reverseIri))))
            {
                throw new JsonLdException(JsonLdErrorCode.InvalidIriMapping, "@reverse of " + _names.Text(term) + " must expand to an IRI or a blank node identifier.");
            }

            definition.Iri = reverseIri;

            int container = _tree.Member(value, Keyword.Container);

            if (container >= 0)
            {
                if (_tree.IsNull(container))
                {
                    definition.Container = Container.None;
                }
                else if (_tree.StringEquals(container, "@set"u8))
                {
                    definition.Container = Container.Set;
                }
                else if (_tree.StringEquals(container, "@index"u8))
                {
                    definition.Container = Container.Index;
                }
                else
                {
                    throw new JsonLdException(JsonLdErrorCode.InvalidReverseProperty, "A reverse property's @container is @set, @index or null.");
                }
            }

            // A reverse term with an @index container may name the index
            // property too (expand-0131); step 20 applies to it as well.
            TakeIndexMapping(active, value, definition, localContext, defined, term);
            definition.Reverse = true;
            active.Terms[term] = definition;
            defined[term] = true;
            return;
        }

        // 14. @id
        bool hasColon = termBytes.Length > 1 && termBytes[1..].IndexOf((byte)':') >= 0;
        bool hasSlash = termBytes.IndexOf((byte)'/') >= 0;

        if (idValue == -2)
        {
            // 14.1 the null mapping: retained so a redefinition is detected.
            definition.Iri = TextRange.None;
        }
        else if (idValue >= 0 && !_tree.StringEquals(idValue, termBytes))
        {
            if (!_tree.IsString(idValue))
            {
                throw new JsonLdException(JsonLdErrorCode.InvalidIriMapping, "The @id of " + _names.Text(term) + " must be a string.");
            }

            ReadOnlySpan<byte> idBytes = _tree.Bytes(idValue);

            if (!IsKeywordText(idBytes) && NameTable.LooksLikeKeyword(idBytes))
            {
                defined[term] = true;
                return;
            }

            TextRange iri = ExpandIri(active, idBytes, documentRelative: false, vocab: true, localContext, defined);

            if (Is(iri, Keyword.Context))
            {
                throw new JsonLdException(JsonLdErrorCode.InvalidKeywordAlias, "@context cannot be aliased.");
            }

            if (iri.IsNone || !(IsKeyword(iri) || IriRef.IsAbsolute(_tree.Bytes(iri)) || IsBlankNode(_tree.Bytes(iri))))
            {
                throw new JsonLdException(JsonLdErrorCode.InvalidIriMapping, "The @id of " + _names.Text(term) + " must expand to an IRI, a blank node identifier or a keyword.");
            }

            definition.Iri = iri;

            // 14.2.4
            bool colonInside = termBytes.Length > 2 && termBytes[1..^1].IndexOf((byte)':') >= 0;

            if (colonInside || hasSlash)
            {
                defined[term] = true;
                TextRange check = ExpandIri(active, termBytes, documentRelative: false, vocab: true, localContext, defined);

                if (!_tree.TextEquals(check, iri))
                {
                    throw new JsonLdException(JsonLdErrorCode.InvalidIriMapping, "The term " + _names.Text(term) + " expands differently from its own @id.");
                }
            }

            // 14.2.5
            if (!hasColon && !hasSlash && simpleTerm)
            {
                ReadOnlySpan<byte> mapping = _tree.Bytes(iri);

                if (IsBlankNode(mapping) || (mapping.Length > 0 && IsGenDelim(mapping[^1])))
                {
                    definition.Prefix = true;
                }
            }
        }
        else if (hasColon)
        {
            // 15. a compact IRI, an IRI or a blank node identifier.
            int colon = termBytes.IndexOf((byte)':');
            ReadOnlySpan<byte> prefix = termBytes[..colon];
            int prefixId = _names.Find(prefix);

            if (prefixId >= 0 && _tree.HasMember(localContext, prefixId))
            {
                CreateTermDefinition(active, localContext, prefixId, defined, baseUrl, protectedDefault, overrideProtected, remoteContexts);
            }

            if (prefixId >= 0 && active.Terms.TryGetValue(prefixId, out TermDefinition? prefixDefinition) && !prefixDefinition.Iri.IsNone)
            {
                definition.Iri = _tree.AddText(_tree.Bytes(prefixDefinition.Iri), termBytes[(colon + 1)..]);
            }
            else
            {
                definition.Iri = _tree.AddText(termBytes);
            }
        }
        else if (hasSlash)
        {
            // 16. a relative IRI reference, expanded against the active
            // context alone: looking the term up in the local context would
            // find itself (expand-er49).
            TextRange iri = ExpandIri(active, termBytes, documentRelative: false, vocab: true, -1, null);

            if (iri.IsNone || !IriRef.IsAbsolute(_tree.Bytes(iri)))
            {
                throw new JsonLdException(JsonLdErrorCode.InvalidIriMapping, "The term " + _names.Text(term) + " does not expand to an IRI.");
            }

            definition.Iri = iri;
        }
        else if (term == Keyword.Type)
        {
            // 17.
            definition.Iri = KeywordText(Keyword.Type);
        }
        else if (!active.Vocab.IsNone)
        {
            // 18.
            definition.Iri = _tree.AddText(_tree.Bytes(active.Vocab), termBytes);
        }
        else
        {
            throw new JsonLdException(JsonLdErrorCode.InvalidIriMapping, "The term " + _names.Text(term) + " has no @id and there is no @vocab.");
        }

        if (value >= 0)
        {
            // 19. @container
            int container = _tree.Member(value, Keyword.Container);

            if (container >= 0)
            {
                definition.Container = ContainerOf(container, _names.Text(term));

                if ((definition.Container & Container.Type) != 0)
                {
                    if (definition.Type.IsNone)
                    {
                        definition.Type = KeywordText(Keyword.Id);
                    }
                    else if (!(Is(definition.Type, Keyword.Id) || Is(definition.Type, Keyword.Vocab)))
                    {
                        throw new JsonLdException(JsonLdErrorCode.InvalidTypeMapping, "A @type container wants a type mapping of @id or @vocab.");
                    }
                }
            }

            // 20. @index
            TakeIndexMapping(active, value, definition, localContext, defined, term);

            // 21. @context
            int scoped = _tree.Member(value, Keyword.Context);

            if (scoped >= 0)
            {
                try
                {
                    Process(active, scoped, baseUrl, [.. remoteContexts], overrideProtected: true, propagate: true, validateScopedContext: false);
                }
                catch (JsonLdException exception)
                {
                    throw new JsonLdException(JsonLdErrorCode.InvalidScopedContext, "The scoped context of " + _names.Text(term) + " is invalid: " + exception.Message);
                }

                definition.LocalContext = scoped;
                definition.HasLocalContext = true;
                definition.BaseUrl = baseUrl;
            }

            // 22. @language
            int language = _tree.Member(value, Keyword.Language);

            if (language >= 0 && !_tree.HasMember(value, Keyword.Type))
            {
                if (_tree.IsNull(language))
                {
                    definition.Language = Mapping.Null;
                }
                else if (_tree.IsString(language))
                {
                    definition.Language = Mapping.Set;
                    definition.LanguageTag = Lowercase(_tree.Bytes(language));
                }
                else
                {
                    throw new JsonLdException(JsonLdErrorCode.InvalidLanguageMapping, "@language must be a string or null.");
                }
            }

            // 23. @direction
            int direction = _tree.Member(value, Keyword.Direction);

            if (direction >= 0 && !_tree.HasMember(value, Keyword.Type))
            {
                definition.Direction = _tree.IsNull(direction) ? Mapping.Null : Mapping.Set;
                definition.DirectionValue = DirectionOf(direction);
            }

            // 24. @nest
            int nest = _tree.Member(value, Keyword.Nest);

            if (nest >= 0)
            {
                if (!_tree.IsString(nest))
                {
                    throw new JsonLdException(JsonLdErrorCode.InvalidNestValue, "@nest must be a string.");
                }

                int nestId = _names.Intern(_tree.Bytes(nest));

                if (NameTable.IsKeyword(nestId) && nestId != Keyword.Nest)
                {
                    throw new JsonLdException(JsonLdErrorCode.InvalidNestValue, "@nest may not be a keyword other than @nest.");
                }

                definition.Nest = nestId;
            }

            // 25. @prefix
            int prefixFlag = _tree.Member(value, Keyword.Prefix);

            if (prefixFlag >= 0)
            {
                if (hasColon || hasSlash)
                {
                    throw new JsonLdException(JsonLdErrorCode.InvalidTermDefinition, "@prefix on a term holding a colon or a slash.");
                }

                if (!_tree.IsBoolean(prefixFlag))
                {
                    throw new JsonLdException(JsonLdErrorCode.InvalidPrefixValue, "@prefix must be true or false.");
                }

                definition.Prefix = _tree.Kind(prefixFlag) == JsonKind.True;

                if (definition.Prefix && IsKeyword(definition.Iri))
                {
                    throw new JsonLdException(JsonLdErrorCode.InvalidTermDefinition, "A prefix cannot map to a keyword.");
                }
            }

            // 26. nothing else.
            for (int member = _tree.First(value); member >= 0; member = _tree.Next(member))
            {
                int key = _tree.NameOf(member);

                if (key is not (Keyword.Id or Keyword.Reverse or Keyword.Container or Keyword.Context or Keyword.Direction or Keyword.Index or Keyword.Language or Keyword.Nest or Keyword.Prefix or Keyword.Protected or Keyword.Type))
                {
                    throw new JsonLdException(JsonLdErrorCode.InvalidTermDefinition, "The definition of " + _names.Text(term) + " has an entry it may not: " + _names.Text(key) + ".");
                }
            }
        }

        // 27. protected redefinition.
        if (!overrideProtected && previous is not null && previous.Protected)
        {
            if (!definition.SameAs(previous, _tree))
            {
                throw new JsonLdException(JsonLdErrorCode.ProtectedTermRedefinition, "The protected term " + _names.Text(term) + " is redefined.");
            }

            definition = previous;
        }

        // 28.
        active.Terms[term] = definition;
        defined[term] = true;
    }

    /// <summary>§4.2.2 step 20: the index mapping of a term with an @index container.</summary>
    private void TakeIndexMapping(ActiveContext active, int value, TermDefinition definition, int localContext, Dictionary<int, bool> defined, int term)
    {
        int index = _tree.Member(value, Keyword.Index);

        if (index < 0)
        {
            return;
        }

        if ((definition.Container & Container.Index) == 0 || !_tree.IsString(index))
        {
            throw new JsonLdException(JsonLdErrorCode.InvalidTermDefinition, "@index on " + _names.Text(term) + " wants an @index container and a string.");
        }

        TextRange expandedIndex = ExpandIri(active, _tree.Bytes(index), documentRelative: false, vocab: true, localContext, defined);

        if (expandedIndex.IsNone || !IriRef.IsAbsolute(_tree.Bytes(expandedIndex)))
        {
            throw new JsonLdException(JsonLdErrorCode.InvalidTermDefinition, "@index of " + _names.Text(term) + " must expand to an IRI.");
        }

        definition.Index = _tree.Range(index);
    }

    private Container ContainerOf(int node, string term)
    {
        Container container = Container.None;
        int count = 0;

        if (_tree.IsString(node))
        {
            container = OneContainer(node, term);
            count = 1;
        }
        else if (_tree.IsArray(node))
        {
            for (int item = _tree.First(node); item >= 0; item = _tree.Next(item))
            {
                Container one = OneContainer(item, term);

                if ((container & one) != 0)
                {
                    throw new JsonLdException(JsonLdErrorCode.InvalidContainerMapping, "The @container of " + term + " repeats a keyword.");
                }

                container |= one;
                count++;
            }
        }
        else
        {
            throw new JsonLdException(JsonLdErrorCode.InvalidContainerMapping, "The @container of " + term + " must be a string or an array of strings.");
        }

        if (count == 1)
        {
            return container;
        }

        // An array: @graph with @id or @index, optionally @set; or @set with
        // one of @index, @graph, @id, @type, @language.
        Container withoutSet = container & ~Container.Set;

        if ((container & Container.Graph) != 0)
        {
            if (withoutSet is (Container.Graph | Container.Id) or (Container.Graph | Container.Index) or Container.Graph)
            {
                return container;
            }

            throw new JsonLdException(JsonLdErrorCode.InvalidContainerMapping, "@graph combines only with @id or @index, and @set.");
        }

        if ((container & Container.Set) != 0 && withoutSet is Container.Index or Container.Id or Container.Type or Container.Language)
        {
            return container;
        }

        throw new JsonLdException(JsonLdErrorCode.InvalidContainerMapping, "The @container of " + term + " is not an allowed combination.");
    }

    private Container OneContainer(int node, string term)
    {
        if (!_tree.IsString(node))
        {
            throw new JsonLdException(JsonLdErrorCode.InvalidContainerMapping, "The @container of " + term + " must name keywords.");
        }

        ReadOnlySpan<byte> text = _tree.Bytes(node);

        if (text.SequenceEqual("@list"u8))
        {
            return Container.List;
        }

        if (text.SequenceEqual("@set"u8))
        {
            return Container.Set;
        }

        if (text.SequenceEqual("@index"u8))
        {
            return Container.Index;
        }

        if (text.SequenceEqual("@id"u8))
        {
            return Container.Id;
        }

        if (text.SequenceEqual("@type"u8))
        {
            return Container.Type;
        }

        if (text.SequenceEqual("@language"u8))
        {
            return Container.Language;
        }

        if (text.SequenceEqual("@graph"u8))
        {
            return Container.Graph;
        }

        throw new JsonLdException(JsonLdErrorCode.InvalidContainerMapping, "The @container of " + term + " names " + Encoding.UTF8.GetString(text) + ", which is not a container keyword.");
    }

    private TextDirection DirectionOf(int node)
    {
        if (_tree.IsNull(node))
        {
            return TextDirection.None;
        }

        if (_tree.StringEquals(node, "ltr"u8))
        {
            return TextDirection.LeftToRight;
        }

        if (_tree.StringEquals(node, "rtl"u8))
        {
            return TextDirection.RightToLeft;
        }

        throw new JsonLdException(JsonLdErrorCode.InvalidBaseDirection, "@direction must be \"ltr\", \"rtl\" or null.");
    }

    // ------------------------------------------------------- IRI expansion

    /// <summary>§5.2.2. Returns the expanded IRI, a keyword's text, or None for null.</summary>
    internal TextRange ExpandIri(
        ActiveContext active,
        ReadOnlySpan<byte> value,
        bool documentRelative,
        bool vocab,
        int localContext,
        Dictionary<int, bool>? defined)
    {
        // 1. and 2.
        int id = _names.Find(value);

        if (NameTable.IsKeyword(id))
        {
            return _keywords[id];
        }

        if (NameTable.LooksLikeKeyword(value))
        {
            return TextRange.None;
        }

        // 3. a term still to be defined in the local context.
        if (localContext >= 0 && id >= 0 && _tree.HasMember(localContext, id) && !(defined is not null && defined.TryGetValue(id, out bool isDefined) && isDefined))
        {
            CreateTermDefinition(active, localContext, id, defined!, TextRange.None, false, false, []);
        }

        // 4. and 5.
        if (id >= 0 && active.Terms.TryGetValue(id, out TermDefinition? definition))
        {
            if (IsKeyword(definition.Iri))
            {
                return definition.Iri;
            }

            if (vocab)
            {
                return definition.Iri;
            }
        }

        // 6. a compact IRI, a blank node identifier or an absolute IRI.
        int colon = value.Length > 1 ? value[1..].IndexOf((byte)':') : -1;

        if (colon >= 0)
        {
            colon += 1;
            ReadOnlySpan<byte> prefix = value[..colon];
            ReadOnlySpan<byte> suffix = value[(colon + 1)..];

            if (prefix.SequenceEqual("_"u8) || suffix.StartsWith("//"u8))
            {
                return _tree.AddText(value);
            }

            int prefixId = _names.Find(prefix);

            if (localContext >= 0 && prefixId >= 0 && _tree.HasMember(localContext, prefixId) && !(defined is not null && defined.TryGetValue(prefixId, out bool prefixDefined) && prefixDefined))
            {
                CreateTermDefinition(active, localContext, prefixId, defined!, TextRange.None, false, false, []);
            }

            if (prefixId >= 0 && active.Terms.TryGetValue(prefixId, out TermDefinition? prefixDefinition) && !prefixDefinition.Iri.IsNone && prefixDefinition.Prefix)
            {
                return _tree.AddText(_tree.Bytes(prefixDefinition.Iri), suffix);
            }

            if (IriRef.IsAbsolute(value))
            {
                return _tree.AddText(value);
            }
        }

        // 7.
        if (vocab && !active.Vocab.IsNone)
        {
            return _tree.AddText(_tree.Bytes(active.Vocab), value);
        }

        // 8.
        if (documentRelative)
        {
            return ResolveOrKeep(active.Base, value);
        }

        // 9.
        return _tree.AddText(value);
    }

    // ------------------------------------------------------------ helpers

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static bool IsBlankNode(ReadOnlySpan<byte> text) => text.StartsWith("_:"u8);

    internal static bool IsGenDelim(byte b) => b is (byte)':' or (byte)'/' or (byte)'?' or (byte)'#' or (byte)'[' or (byte)']' or (byte)'@';

    private bool IsKeywordText(ReadOnlySpan<byte> text) => NameTable.IsKeyword(_names.Find(text));

    /// <summary>Resolves against the base, or keeps the reference when there is no base to resolve against.</summary>
    internal TextRange ResolveOrKeep(TextRange baseIri, ReadOnlySpan<byte> reference)
    {
        if (baseIri.IsNone || IriRef.IsAbsolute(reference))
        {
            return _tree.AddText(reference);
        }

        return Resolve(baseIri, reference);
    }

    internal TextRange Resolve(TextRange baseIri, ReadOnlySpan<byte> reference)
    {
        ReadOnlySpan<byte> baseBytes = _tree.Bytes(baseIri);
        int length = IriRef.ResolveLength(baseBytes, reference);

        if (length < 0)
        {
            return _tree.AddText(reference);
        }

        Span<byte> destination = _tree.ReserveText(length);

        if (!IriRef.TryResolve(_tree.Bytes(baseIri), reference, destination, out int written))
        {
            return _tree.AddText(reference);
        }

        return _tree.CommitText(written);
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    [DesignDecision(typeof(JsonLdOverUtf8Json.LanguageTagsAreLowercased), Scope = ExceptionScope.HotPath)]
    internal TextRange Lowercase(ReadOnlySpan<byte> text)
    {
        Span<byte> destination = _tree.ReserveText(text.Length);

        for (int i = 0; i < text.Length; i++)
        {
            byte b = text[i];
            destination[i] = b >= (byte)'A' && b <= (byte)'Z' ? (byte)(b | 0x20) : b;
        }

        return _tree.CommitText(text.Length);
    }

    [DesignDecision(typeof(JsonLdOverUtf8Json.TheFirstErrorEndsTheParse), Scope = ExceptionScope.Boundary)]
    internal string Text(TextRange range) => Encoding.UTF8.GetString(_tree.Bytes(range));
}
