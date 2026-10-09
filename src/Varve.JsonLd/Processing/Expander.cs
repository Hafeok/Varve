// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Iri;
using Varve.JsonLd.Json;
using Varve.JsonLd.Model;
using Varve.Rdf;

namespace Varve.JsonLd.Processing;

/// <summary>
/// The expansion algorithm (JSON-LD 1.1 API §5.1.2) and value expansion
/// (§5.3.2), writing the expanded document as new nodes of the same tree
/// (ADR 0112, <c>ExpansionWritesIntoTheTree</c>).
/// </summary>
/// <remarks>
/// Node indices stand for JSON values and -1 for null. An active property is
/// a name id, -1 for null; <see cref="Keyword.Graph"/> and
/// <see cref="Keyword.Reverse"/> are the two keywords the algorithm passes as
/// active property.
/// </remarks>
[DesignDecision(typeof(JsonLdOverUtf8Json.ExpansionWritesIntoTheTree), Scope = ExceptionScope.HotPath)]
internal sealed class Expander
{
    private readonly JsonTree _tree;
    private readonly NameTable _names;
    private readonly ContextProcessor _contexts;

    internal Expander(JsonTree tree, ContextProcessor contexts)
    {
        _tree = tree;
        _names = tree.Names;
        _contexts = contexts;
    }

    /// <summary>§5.1.2. Returns the expanded element, or -1 for null.</summary>
    internal int Expand(ActiveContext active, int activeProperty, int element, TextRange baseUrl, bool fromMap = false)
    {
        // 1.
        if (element < 0 || _tree.IsNull(element))
        {
            return -1;
        }

        // 3.
        TermDefinition? propertyDefinition = activeProperty >= 0 ? active.Term(activeProperty) : null;
        bool propertyScoped = propertyDefinition is not null && propertyDefinition.HasLocalContext;

        // 4. a scalar.
        if (_tree.IsScalar(element))
        {
            if (activeProperty < 0 || activeProperty == Keyword.Graph)
            {
                return -1;
            }

            if (propertyScoped)
            {
                active = _contexts.Process(active, propertyDefinition!.LocalContext, propertyDefinition.BaseUrl);
            }

            return ExpandValue(active, activeProperty, element);
        }

        // 5. an array.
        if (_tree.IsArray(element))
        {
            int result = _tree.AddArray();

            for (int item = _tree.First(element); item >= 0; item = _tree.Next(item))
            {
                int expanded = Expand(active, activeProperty, item, baseUrl, fromMap);

                if (expanded < 0)
                {
                    continue;
                }

                if (propertyDefinition is not null && (propertyDefinition.Container & Container.List) != 0 && _tree.IsArray(expanded))
                {
                    int list = _tree.AddObject();
                    _tree.AddMember(list, Keyword.List, expanded);
                    expanded = list;
                }

                _tree.AppendAll(result, expanded);
            }

            return result;
        }

        // 6. an object.
        return ExpandObject(active, activeProperty, element, baseUrl, fromMap, propertyDefinition);
    }

    private int ExpandObject(ActiveContext active, int activeProperty, int element, TextRange baseUrl, bool fromMap, TermDefinition? propertyDefinition)
    {
        // 7. a non-propagated context ends at the next node object.
        if (active.Previous is not null && !fromMap)
        {
            bool hasValue = false;
            bool onlyId = _tree.Count(element) == 1;

            for (int member = _tree.First(element); member >= 0; member = _tree.Next(member))
            {
                int expanded = _contexts.KeywordOf(_contexts.ExpandIri(active, _tree.NameBytes(member), false, true, -1, null));

                if (expanded == Keyword.Value)
                {
                    hasValue = true;
                }

                if (expanded != Keyword.Id)
                {
                    onlyId = false;
                }
            }

            if (!hasValue && !onlyId)
            {
                active = active.Previous;
            }
        }

        // 8. the property-scoped context.
        if (propertyDefinition is not null && propertyDefinition.HasLocalContext)
        {
            active = _contexts.Process(active, propertyDefinition.LocalContext, propertyDefinition.BaseUrl, overrideProtected: true);
        }

        // 9. the element's own context.
        int context = _tree.Member(element, Keyword.Context);

        if (context >= 0 || _tree.HasMember(element, Keyword.Context))
        {
            active = _contexts.Process(active, context, baseUrl);
        }

        // 10.
        ActiveContext typeScoped = active;

        // Keys are processed in lexicographical order, which the algorithm
        // allows and which makes the output deterministic.
        _tree.SortMembers(element);

        // 11. type-scoped contexts, and 12. the input type.
        TextRange inputType = TextRange.None;

        for (int member = _tree.First(element); member >= 0; member = _tree.Next(member))
        {
            if (!_contexts.Is(_contexts.ExpandIri(active, _tree.NameBytes(member), false, true, -1, null), Keyword.Type))
            {
                continue;
            }

            // The terms are visited in lexicographical order (11.2) over a
            // copy, so that the @type array keeps the document's order.
            int value = _tree.ValueOf(member);

            if (_tree.IsArray(value))
            {
                value = _tree.Copy(value);
                _tree.SortStrings(value);
            }

            int last = -1;

            for (int term = _tree.IsArray(value) ? _tree.First(value) : value; term >= 0; term = _tree.IsArray(value) ? _tree.Next(term) : -1)
            {
                if (!_tree.IsString(term))
                {
                    continue;
                }

                last = term;
                int termId = _names.Find(_tree.Bytes(term));
                TermDefinition? definition = termId >= 0 ? typeScoped.Term(termId) : null;

                if (definition is not null && definition.HasLocalContext)
                {
                    active = _contexts.Process(active, definition.LocalContext, definition.BaseUrl, propagate: false);
                }
            }

            if (last >= 0 && inputType.IsNone)
            {
                inputType = _contexts.ExpandIri(active, _tree.Bytes(last), false, true, -1, null);
            }
        }

        // 13.
        int result = _tree.AddObject();
        List<int>? nests = null;
        ExpandMembers(active, typeScoped, activeProperty, element, baseUrl, result, inputType, ref nests);

        // 14. nested values.
        if (nests is not null)
        {
            ExpandNests(active, typeScoped, nests, baseUrl, result, inputType);
        }

        // 15. a value object.
        int valueMember = _tree.FindMember(result, Keyword.Value);

        if (valueMember >= 0)
        {
            int count = _tree.Count(result);
            bool hasType = _tree.HasMember(result, Keyword.Type);
            bool hasLanguage = _tree.HasMember(result, Keyword.Language);
            bool hasDirection = _tree.HasMember(result, Keyword.Direction);
            int allowed = 1 + (hasType ? 1 : 0) + (hasLanguage ? 1 : 0) + (hasDirection ? 1 : 0) + (_tree.HasMember(result, Keyword.Index) ? 1 : 0);

            if (count != allowed || (hasType && (hasLanguage || hasDirection)))
            {
                throw new JsonLdException(JsonLdErrorCode.InvalidValueObject, "A value object has @value and at most @type, @language, @direction and @index, and not @type with @language or @direction.");
            }

            int typeValue = _tree.Member(result, Keyword.Type);
            int value = _tree.ValueOf(valueMember);

            if (hasType && _tree.StringEquals(typeValue, "@json"u8))
            {
                // 15.2 a JSON literal: any value.
            }
            else if (value < 0 || _tree.IsNull(value) || (_tree.IsArray(value) && _tree.Count(value) == 0))
            {
                return -1;
            }
            else if (hasLanguage && !_tree.IsString(value))
            {
                throw new JsonLdException(JsonLdErrorCode.InvalidLanguageTaggedValue, "A language-tagged value must be a string.");
            }
            else if (hasType && !(_tree.IsString(typeValue) && IriRef.IsAbsolute(_tree.Bytes(typeValue))))
            {
                throw new JsonLdException(JsonLdErrorCode.InvalidTypedValue, "The @type of a value must be an IRI.");
            }
        }
        else if (_tree.FindMember(result, Keyword.Type) is int typeMember && typeMember >= 0 && !_tree.IsArray(_tree.ValueOf(typeMember)))
        {
            // 16. @type as an array.
            _tree.SetMember(result, Keyword.Type, _tree.AddArrayOf(_tree.ValueOf(typeMember)));
        }
        else if (_tree.HasMember(result, Keyword.Set) || _tree.HasMember(result, Keyword.List))
        {
            // 17. @set and @list.
            int count = _tree.Count(result);

            if (count > 2 || (count == 2 && !_tree.HasMember(result, Keyword.Index)))
            {
                throw new JsonLdException(JsonLdErrorCode.InvalidSetOrListObject, "A @set or @list object has at most an @index besides.");
            }

            if (_tree.HasMember(result, Keyword.Set))
            {
                return _tree.Member(result, Keyword.Set);
            }
        }

        // 18. only @language.
        if (_tree.HasOnly(result, Keyword.Language))
        {
            return -1;
        }

        // 19. free-floating values.
        if (activeProperty < 0 || activeProperty == Keyword.Graph)
        {
            int count = _tree.Count(result);

            if (count == 0 || _tree.HasMember(result, Keyword.Value) || _tree.HasMember(result, Keyword.List))
            {
                return -1;
            }

            if (_tree.HasOnly(result, Keyword.Id))
            {
                return -1;
            }
        }

        return result;
    }

    /// <summary>
    /// Step 14 of §5.1.2: each nested value's members are expanded into the
    /// same result, with the nesting key as the active property so that a
    /// property-scoped context on a term aliasing @nest applies
    /// (expand-c037, c038), and its own nests likewise.
    /// </summary>
    private void ExpandNests(ActiveContext active, ActiveContext typeScoped, List<int> nests, TextRange baseUrl, int result, TextRange inputType)
    {
        foreach (int nest in nests)
        {
            int nestingKey = _tree.NameOf(nest);
            int nestedValues = _tree.ValueOf(nest);
            ActiveContext nestedContext = active;
            TermDefinition? nestDefinition = active.Term(nestingKey);

            if (nestDefinition is not null && nestDefinition.HasLocalContext)
            {
                nestedContext = _contexts.Process(active, nestDefinition.LocalContext, nestDefinition.BaseUrl, overrideProtected: true);
            }

            for (int nested = _tree.IsArray(nestedValues) ? _tree.First(nestedValues) : nestedValues; nested >= 0; nested = _tree.IsArray(nestedValues) ? _tree.Next(nested) : -1)
            {
                if (!_tree.IsObject(nested))
                {
                    throw new JsonLdException(JsonLdErrorCode.InvalidNestValue, "A nested value must be an object.");
                }

                for (int member = _tree.First(nested); member >= 0; member = _tree.Next(member))
                {
                    if (_contexts.Is(_contexts.ExpandIri(nestedContext, _tree.NameBytes(member), false, true, -1, null), Keyword.Value))
                    {
                        throw new JsonLdException(JsonLdErrorCode.InvalidNestValue, "A nested value may not be a value object.");
                    }
                }

                _tree.SortMembers(nested);
                List<int>? innerNests = null;
                ExpandMembers(nestedContext, typeScoped, nestingKey, nested, baseUrl, result, inputType, ref innerNests);

                if (innerNests is not null)
                {
                    ExpandNests(nestedContext, typeScoped, innerNests, baseUrl, result, inputType);
                }
            }
        }
    }

    /// <summary>Step 13 of §5.1.2, over one object's members, into <paramref name="result"/>.</summary>
    private void ExpandMembers(ActiveContext active, ActiveContext typeScoped, int activeProperty, int element, TextRange baseUrl, int result, TextRange inputType, ref List<int>? nests)
    {
        bool inputIsJson = _contexts.Is(inputType, Keyword.Json);

        for (int member = _tree.First(element); member >= 0; member = _tree.Next(member))
        {
            int key = _tree.NameOf(member);
            int value = _tree.ValueOf(member);

            // 13.1
            if (key == Keyword.Context)
            {
                continue;
            }

            // 13.2 and 13.3
            TextRange expandedProperty = _contexts.ExpandIri(active, _names.Bytes(key), false, true, -1, null);

            if (expandedProperty.IsNone)
            {
                continue;
            }

            int keyword = _contexts.KeywordOf(expandedProperty);
            ReadOnlySpan<byte> propertyBytes = _tree.Bytes(expandedProperty);

            if (keyword < 0 && propertyBytes.IndexOf((byte)':') < 0)
            {
                continue;
            }

            int propertyId = keyword >= 0 ? keyword : _names.Intern(propertyBytes);

            // 13.4 a keyword.
            if (keyword >= 0)
            {
                if (activeProperty == Keyword.Reverse)
                {
                    throw new JsonLdException(JsonLdErrorCode.InvalidReversePropertyMap, "A keyword inside @reverse.");
                }

                if (_tree.HasMember(result, keyword) && keyword is not (Keyword.Included or Keyword.Type))
                {
                    throw new JsonLdException(JsonLdErrorCode.CollidingKeywords, "Two entries expand to " + _names.Text(keyword) + ".");
                }

                int expandedValue;

                switch (keyword)
                {
                    case Keyword.Id:
                        if (!_tree.IsString(value))
                        {
                            throw new JsonLdException(JsonLdErrorCode.InvalidIdValue, "@id must be a string.");
                        }

                    {
                        // An @id shaped like a keyword expands to null, which is
                        // kept as JSON null (expand-0122).
                        TextRange expandedId = _contexts.ExpandIri(active, _tree.Bytes(value), true, false, -1, null);
                        expandedValue = expandedId.IsNone ? -1 : _tree.AddString(expandedId);
                        break;
                    }

                    case Keyword.Type:
                    {
                        int types = _tree.AddArray();

                        if (_tree.IsString(value))
                        {
                            _tree.Append(types, _tree.AddString(_contexts.ExpandIri(typeScoped, _tree.Bytes(value), true, true, -1, null)));
                            expandedValue = _tree.First(types);
                        }
                        else if (_tree.IsArray(value))
                        {
                            for (int item = _tree.First(value); item >= 0; item = _tree.Next(item))
                            {
                                if (!_tree.IsString(item))
                                {
                                    throw new JsonLdException(JsonLdErrorCode.InvalidTypeValue, "@type must be a string or an array of strings.");
                                }

                                _tree.Append(types, _tree.AddString(_contexts.ExpandIri(typeScoped, _tree.Bytes(item), true, true, -1, null)));
                            }

                            expandedValue = types;
                        }
                        else
                        {
                            throw new JsonLdException(JsonLdErrorCode.InvalidTypeValue, "@type must be a string or an array of strings.");
                        }

                        int existing = _tree.Member(result, Keyword.Type);

                        if (existing >= 0)
                        {
                            int all = _tree.AddArray();
                            _tree.AppendAll(all, existing);
                            _tree.AppendAll(all, expandedValue);
                            expandedValue = all;
                        }

                        break;
                    }

                    case Keyword.Graph:
                    {
                        int expanded = Expand(active, Keyword.Graph, value, baseUrl);
                        expandedValue = AsArray(expanded);
                        break;
                    }

                    case Keyword.Included:
                    {
                        int expandedIncluded = Expand(active, -1, value, baseUrl);

                        if (expandedIncluded < 0)
                        {
                            throw new JsonLdException(JsonLdErrorCode.InvalidIncludedValue, "@included holds node objects only.");
                        }

                        int expanded = AsArray(expandedIncluded);

                        for (int item = _tree.First(expanded); item >= 0; item = _tree.Next(item))
                        {
                            if (!IsNodeObject(item))
                            {
                                throw new JsonLdException(JsonLdErrorCode.InvalidIncludedValue, "@included holds node objects only.");
                            }
                        }

                        int existing = _tree.Member(result, Keyword.Included);

                        if (existing >= 0)
                        {
                            int all = _tree.AddArray();
                            _tree.AppendAll(all, existing);
                            _tree.AppendAll(all, expanded);
                            expanded = all;
                        }

                        expandedValue = expanded;
                        break;
                    }

                    case Keyword.Value:
                        if (inputIsJson)
                        {
                            expandedValue = value;
                        }
                        else if (_tree.IsScalar(value))
                        {
                            expandedValue = value;

                            if (_tree.IsNull(value))
                            {
                                _tree.SetMember(result, Keyword.Value, _tree.AddNull());
                                continue;
                            }
                        }
                        else
                        {
                            throw new JsonLdException(JsonLdErrorCode.InvalidValueObjectValue, "@value must be a scalar or null.");
                        }

                        break;

                    case Keyword.Language:
                        if (!_tree.IsString(value))
                        {
                            throw new JsonLdException(JsonLdErrorCode.InvalidLanguageTaggedString, "@language must be a string.");
                        }

                        expandedValue = _tree.AddString(_contexts.Lowercase(_tree.Bytes(value)));
                        break;

                    case Keyword.Direction:
                        if (!(_tree.StringEquals(value, "ltr"u8) || _tree.StringEquals(value, "rtl"u8)))
                        {
                            throw new JsonLdException(JsonLdErrorCode.InvalidBaseDirection, "@direction must be \"ltr\" or \"rtl\".");
                        }

                        expandedValue = value;
                        break;

                    case Keyword.Index:
                        if (!_tree.IsString(value))
                        {
                            throw new JsonLdException(JsonLdErrorCode.InvalidIndexValue, "@index must be a string.");
                        }

                        expandedValue = value;
                        break;

                    case Keyword.List:
                        if (activeProperty < 0 || activeProperty == Keyword.Graph)
                        {
                            continue;
                        }

                        expandedValue = AsArray(Expand(active, activeProperty, value, baseUrl));
                        break;

                    case Keyword.Set:
                        expandedValue = Expand(active, activeProperty, value, baseUrl);
                        break;

                    case Keyword.Reverse:
                    {
                        if (!_tree.IsObject(value))
                        {
                            throw new JsonLdException(JsonLdErrorCode.InvalidReverseValue, "@reverse must be an object.");
                        }

                        int expanded = Expand(active, Keyword.Reverse, value, baseUrl);

                        if (expanded < 0)
                        {
                            continue;
                        }

                        // 13.4.13.2 reverse of reverse is forward.
                        int innerReverse = _tree.Member(expanded, Keyword.Reverse);

                        if (innerReverse >= 0)
                        {
                            for (int property = _tree.First(innerReverse); property >= 0; property = _tree.Next(property))
                            {
                                AppendTo(result, _tree.NameOf(property), _tree.ValueOf(property));
                            }
                        }

                        // 13.4.13.3
                        if (_tree.Count(expanded) > (innerReverse >= 0 ? 1 : 0))
                        {
                            int reverseMap = ReverseMap(result);

                            for (int property = _tree.First(expanded); property >= 0; property = _tree.Next(property))
                            {
                                if (_tree.NameOf(property) == Keyword.Reverse)
                                {
                                    continue;
                                }

                                int items = _tree.ValueOf(property);

                                for (int item = _tree.IsArray(items) ? _tree.First(items) : items; item >= 0; item = _tree.IsArray(items) ? _tree.Next(item) : -1)
                                {
                                    if (IsValueObject(item) || IsListObject(item))
                                    {
                                        throw new JsonLdException(JsonLdErrorCode.InvalidReversePropertyValue, "A reverse property's value is a node object.");
                                    }

                                    AppendTo(reverseMap, _tree.NameOf(property), item);
                                }
                            }
                        }

                        continue;
                    }

                    case Keyword.Nest:
                        nests ??= [];
                        nests.Add(member);
                        continue;

                    default:
                        // A framing keyword, or @context handled above: dropped.
                        continue;
                }

                // 13.4.16
                if (expandedValue < 0 && keyword == Keyword.Value && !inputIsJson)
                {
                    continue;
                }

                if (expandedValue < 0)
                {
                    expandedValue = _tree.AddNull();
                }

                _tree.SetMember(result, keyword, expandedValue);
                continue;
            }

            // 13.5
            TermDefinition? definition = active.Term(key);
            Container container = definition?.Container ?? Container.None;
            int expandedValue2;

            if (definition is not null && _contexts.Is(definition.Type, Keyword.Json))
            {
                // 13.6 a JSON literal.
                expandedValue2 = _tree.AddObject();
                _tree.AddMember(expandedValue2, Keyword.Value, value);
                _tree.AddMember(expandedValue2, Keyword.Type, _tree.AddStringOfName(Keyword.Json));
            }
            else if ((container & Container.Language) != 0 && _tree.IsObject(value))
            {
                // 13.7 a language map.
                expandedValue2 = _tree.AddArray();
                _tree.SortMembers(value);

                for (int entry = _tree.First(value); entry >= 0; entry = _tree.Next(entry))
                {
                    int languageValue = _tree.ValueOf(entry);
                    ReadOnlySpan<byte> language = _tree.NameBytes(entry);
                    bool none = language.SequenceEqual("@none"u8) || _contexts.Is(_contexts.ExpandIri(active, language, false, true, -1, null), Keyword.None);

                    for (int item = _tree.IsArray(languageValue) ? _tree.First(languageValue) : languageValue; item >= 0; item = _tree.IsArray(languageValue) ? _tree.Next(item) : -1)
                    {
                        if (_tree.IsNull(item))
                        {
                            continue;
                        }

                        if (!_tree.IsString(item))
                        {
                            throw new JsonLdException(JsonLdErrorCode.InvalidLanguageMapValue, "A language map holds strings.");
                        }

                        int v = _tree.AddObject();
                        _tree.AddMember(v, Keyword.Value, item);

                        if (!none)
                        {
                            _tree.AddMember(v, Keyword.Language, _tree.AddString(_contexts.Lowercase(language)));
                        }

                        TextDirection direction = definition!.Direction == Mapping.Absent ? active.DefaultDirection
                            : definition.Direction == Mapping.Set ? definition.DirectionValue : TextDirection.None;

                        if (direction != TextDirection.None)
                        {
                            _tree.AddMember(v, Keyword.Direction, _tree.AddString(direction == TextDirection.LeftToRight ? "ltr"u8 : "rtl"u8));
                        }

                        _tree.Append(expandedValue2, v);
                    }
                }
            }
            else if ((container & (Container.Index | Container.Type | Container.Id)) != 0 && _tree.IsObject(value))
            {
                // 13.8 an index, type or id map.
                expandedValue2 = _tree.AddArray();
                int indexKey = definition!.Index.IsNone ? Keyword.Index : _names.Intern(_tree.Bytes(definition.Index));
                _tree.SortMembers(value);

                for (int entry = _tree.First(value); entry >= 0; entry = _tree.Next(entry))
                {
                    int index = _tree.NameOf(entry);
                    int indexValue = _tree.ValueOf(entry);
                    ActiveContext mapContext = active;

                    if ((container & (Container.Id | Container.Type)) != 0)
                    {
                        mapContext = active.Previous ?? active;

                        if ((container & Container.Type) != 0)
                        {
                            TermDefinition? indexDefinition = mapContext.Term(index);

                            if (indexDefinition is not null && indexDefinition.HasLocalContext)
                            {
                                mapContext = _contexts.Process(mapContext, indexDefinition.LocalContext, indexDefinition.BaseUrl);
                            }
                        }
                    }

                    TextRange expandedIndex = _contexts.ExpandIri(active, _names.Bytes(index), false, true, -1, null);
                    bool indexIsNone = _contexts.Is(expandedIndex, Keyword.None);
                    int expandedItems = AsArray(Expand(mapContext, key, indexValue, baseUrl, fromMap: true));

                    for (int item = _tree.First(expandedItems); item >= 0;)
                    {
                        int next = _tree.Next(item);

                        if ((container & Container.Graph) != 0 && !IsGraphObject(item))
                        {
                            int graph = _tree.AddObject();
                            _tree.AddMember(graph, Keyword.Graph, _tree.AddArrayOf(item));
                            item = graph;
                        }

                        if ((container & Container.Index) != 0 && indexKey != Keyword.Index && !indexIsNone)
                        {
                            // 13.8.3.7.2 a property-valued index.
                            int reExpanded = ExpandValue(active, indexKey, _tree.AddStringOfName(index));
                            TextRange expandedIndexKey = _contexts.ExpandIri(active, _names.Bytes(indexKey), false, true, -1, null);
                            int indexProperty = _names.Intern(_tree.Bytes(expandedIndexKey));
                            int values = _tree.AddArrayOf(reExpanded);
                            int existing = _tree.Member(item, indexProperty);

                            if (existing >= 0)
                            {
                                _tree.AppendAll(values, existing);
                            }

                            _tree.SetMember(item, indexProperty, values);

                            if (IsValueObject(item))
                            {
                                throw new JsonLdException(JsonLdErrorCode.InvalidValueObject, "A property-valued index on a value object.");
                            }
                        }
                        else if ((container & Container.Index) != 0 && !_tree.HasMember(item, Keyword.Index) && !indexIsNone)
                        {
                            _tree.AddMember(item, Keyword.Index, _tree.AddStringOfName(index));
                        }
                        else if ((container & Container.Id) != 0 && !_tree.HasMember(item, Keyword.Id) && !indexIsNone)
                        {
                            _tree.AddMember(item, Keyword.Id, _tree.AddString(_contexts.ExpandIri(active, _names.Bytes(index), true, false, -1, null)));
                        }
                        else if ((container & Container.Type) != 0 && !indexIsNone)
                        {
                            int types = _tree.AddArrayOf(_tree.AddString(expandedIndex));
                            int existing = _tree.Member(item, Keyword.Type);

                            if (existing >= 0)
                            {
                                _tree.AppendAll(types, existing);
                            }

                            _tree.SetMember(item, Keyword.Type, types);
                        }

                        _tree.Append(expandedValue2, item);
                        item = next;
                    }
                }
            }
            else
            {
                // 13.9
                expandedValue2 = Expand(active, key, value, baseUrl);
            }

            // 13.10
            if (expandedValue2 < 0)
            {
                continue;
            }

            // 13.11
            if ((container & Container.List) != 0 && !IsListObject(expandedValue2))
            {
                int list = _tree.AddObject();
                _tree.AddMember(list, Keyword.List, AsArray(expandedValue2));
                expandedValue2 = list;
            }

            // 13.12
            if ((container & Container.Graph) != 0 && (container & (Container.Id | Container.Index)) == 0)
            {
                int items = AsArray(expandedValue2);
                int graphs = _tree.AddArray();

                // 13.12.2.1: every item is wrapped, a graph object included
                // (expand-0081: "Creates an @graph container if value is a
                // graph"); only the index and id maps of 13.8 ask first.
                for (int item = _tree.First(items); item >= 0;)
                {
                    int next = _tree.Next(item);
                    int graph = _tree.AddObject();
                    _tree.AddMember(graph, Keyword.Graph, _tree.AddArrayOf(item));
                    _tree.Append(graphs, graph);
                    item = next;
                }

                expandedValue2 = graphs;
            }

            // 13.13 a reverse property.
            if (definition is not null && definition.Reverse)
            {
                int reverseMap = ReverseMap(result);
                int items = AsArray(expandedValue2);

                for (int item = _tree.First(items); item >= 0;)
                {
                    int next = _tree.Next(item);

                    if (IsValueObject(item) || IsListObject(item))
                    {
                        throw new JsonLdException(JsonLdErrorCode.InvalidReversePropertyValue, "A reverse property's value is a node object.");
                    }

                    AppendTo(reverseMap, propertyId, item);
                    item = next;
                }

                continue;
            }

            // 13.14
            AppendTo(result, propertyId, expandedValue2);
        }
    }

    /// <summary>§5.3.2.</summary>
    internal int ExpandValue(ActiveContext active, int activeProperty, int value)
    {
        TermDefinition? definition = activeProperty >= 0 ? active.Term(activeProperty) : null;
        int typeKeyword = definition is null ? -1 : _contexts.KeywordOf(definition.Type);

        if (_tree.IsString(value))
        {
            if (typeKeyword == Keyword.Id)
            {
                int node = _tree.AddObject();
                _tree.AddMember(node, Keyword.Id, _tree.AddString(_contexts.ExpandIri(active, _tree.Bytes(value), true, false, -1, null)));
                return node;
            }

            if (typeKeyword == Keyword.Vocab)
            {
                int node = _tree.AddObject();
                _tree.AddMember(node, Keyword.Id, _tree.AddString(_contexts.ExpandIri(active, _tree.Bytes(value), true, true, -1, null)));
                return node;
            }
        }

        int result = _tree.AddObject();
        _tree.AddMember(result, Keyword.Value, value);

        if (definition is not null && !definition.Type.IsNone && typeKeyword is not (Keyword.Id or Keyword.Vocab or Keyword.None))
        {
            _tree.AddMember(result, Keyword.Type, _tree.AddString(definition.Type));
        }
        else if (_tree.IsString(value))
        {
            TextRange language = definition is null || definition.Language == Mapping.Absent ? active.DefaultLanguage
                : definition.Language == Mapping.Set ? definition.LanguageTag : TextRange.None;
            TextDirection direction = definition is null || definition.Direction == Mapping.Absent ? active.DefaultDirection
                : definition.Direction == Mapping.Set ? definition.DirectionValue : TextDirection.None;

            if (!language.IsNone)
            {
                _tree.AddMember(result, Keyword.Language, _tree.AddString(language));
            }

            if (direction != TextDirection.None)
            {
                _tree.AddMember(result, Keyword.Direction, _tree.AddString(direction == TextDirection.LeftToRight ? "ltr"u8 : "rtl"u8));
            }
        }

        return result;
    }

    // ------------------------------------------------------------ helpers

    /// <summary>The value as an array: itself if it is one, else a new array holding it; an empty array for null.</summary>
    private int AsArray(int value)
    {
        if (value < 0)
        {
            return _tree.AddArray();
        }

        return _tree.IsArray(value) ? value : _tree.AddArrayOf(value);
    }

    /// <summary>Appends the items of <paramref name="value"/> (or the value) to the array member <paramref name="property"/> of <paramref name="obj"/>, creating it.</summary>
    private void AppendTo(int obj, int property, int value)
    {
        int existing = _tree.Member(obj, property);

        if (existing < 0)
        {
            existing = _tree.AddArray();
            _tree.AddMember(obj, property, existing);
        }
        else if (!_tree.IsArray(existing))
        {
            int array = _tree.AddArrayOf(existing);
            _tree.SetMember(obj, property, array);
            existing = array;
        }

        _tree.AppendAll(existing, value);
    }

    private int ReverseMap(int result)
    {
        int map = _tree.Member(result, Keyword.Reverse);

        if (map < 0)
        {
            map = _tree.AddObject();
            _tree.AddMember(result, Keyword.Reverse, map);
        }

        return map;
    }

    internal bool IsValueObject(int node) => _tree.IsObject(node) && _tree.HasMember(node, Keyword.Value);

    internal bool IsListObject(int node) => _tree.IsObject(node) && _tree.HasMember(node, Keyword.List);

    internal bool IsNodeObject(int node) =>
        _tree.IsObject(node) && !_tree.HasMember(node, Keyword.Value) && !_tree.HasMember(node, Keyword.List) && !_tree.HasMember(node, Keyword.Set);

    /// <summary>A graph object: @graph with at most @id and @index besides.</summary>
    internal bool IsGraphObject(int node)
    {
        if (!_tree.IsObject(node) || !_tree.HasMember(node, Keyword.Graph))
        {
            return false;
        }

        for (int member = _tree.First(node); member >= 0; member = _tree.Next(member))
        {
            if (_tree.NameOf(member) is not (Keyword.Graph or Keyword.Id or Keyword.Index))
            {
                return false;
            }
        }

        return true;
    }
}
