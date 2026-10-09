// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.JsonLd.Model;

/// <summary>
/// The specification's <c>JsonLdErrorCode</c> enumeration (JSON-LD 1.1 API
/// §9.2), one value per code, so that a caller and the W3C suite name the
/// same thing (ADR 0112). <see cref="JsonLdErrorCodes.Text"/> gives the
/// specification's spelling.
/// </summary>
public enum JsonLdErrorCode
{
    /// <summary>No error.</summary>
    None,

    /// <summary>Two properties expand to the same keyword.</summary>
    CollidingKeywords,

    /// <summary>Multiple conflicting indexes for a node.</summary>
    ConflictingIndexes,

    /// <summary>The maximum number of @context IRIs to process was exceeded.</summary>
    ContextOverflow,

    /// <summary>A cycle in IRI mappings.</summary>
    CyclicIriMapping,

    /// <summary>An @id value that is not a string.</summary>
    InvalidIdValue,

    /// <summary>An @import value that is not a string.</summary>
    InvalidImportValue,

    /// <summary>An @included value that is not a node object or an array of them.</summary>
    InvalidIncludedValue,

    /// <summary>An @index value that is not a string.</summary>
    InvalidIndexValue,

    /// <summary>An @nest value that is not an object or a term resolving to @nest.</summary>
    InvalidNestValue,

    /// <summary>An @prefix value that is not a boolean.</summary>
    InvalidPrefixValue,

    /// <summary>An @propagate value that is not a boolean.</summary>
    InvalidPropagateValue,

    /// <summary>An @protected value that is not a boolean.</summary>
    InvalidProtectedValue,

    /// <summary>An @reverse value that is not an object.</summary>
    InvalidReverseValue,

    /// <summary>An @version value other than 1.1.</summary>
    InvalidVersionValue,

    /// <summary>A base direction other than ltr, rtl or null.</summary>
    InvalidBaseDirection,

    /// <summary>A base IRI that is not an IRI.</summary>
    InvalidBaseIri,

    /// <summary>An @container value the specification does not allow.</summary>
    InvalidContainerMapping,

    /// <summary>A context entry the specification does not allow.</summary>
    InvalidContextEntry,

    /// <summary>A context that would nullify a protected term.</summary>
    InvalidContextNullification,

    /// <summary>A default language that is not a string.</summary>
    InvalidDefaultLanguage,

    /// <summary>A term's IRI mapping that is not an IRI, a compact IRI, a blank node identifier or a keyword.</summary>
    InvalidIriMapping,

    /// <summary>An rdf:JSON literal whose lexical form is not JSON.</summary>
    InvalidJsonLiteral,

    /// <summary>A keyword aliased to something that is not a keyword.</summary>
    InvalidKeywordAlias,

    /// <summary>A language map value that is not a string or an array of strings.</summary>
    InvalidLanguageMapValue,

    /// <summary>A language mapping that is not a string or null.</summary>
    InvalidLanguageMapping,

    /// <summary>A language-tagged string whose @language is not a string.</summary>
    InvalidLanguageTaggedString,

    /// <summary>A language-tagged value that is not a string.</summary>
    InvalidLanguageTaggedValue,

    /// <summary>A local context that is not an object, a string, an array of them or null.</summary>
    InvalidLocalContext,

    /// <summary>A remote context whose document has no @context.</summary>
    InvalidRemoteContext,

    /// <summary>A reverse property mapped to something that is not an IRI.</summary>
    InvalidReverseProperty,

    /// <summary>An @reverse map that is not an object.</summary>
    InvalidReversePropertyMap,

    /// <summary>A reverse property whose value is a value object or a list.</summary>
    InvalidReversePropertyValue,

    /// <summary>A scoped context that cannot be processed.</summary>
    InvalidScopedContext,

    /// <summary>A @set or @list object with members other than @index.</summary>
    InvalidSetOrListObject,

    /// <summary>A term definition that is not an object, a string or null, or has forbidden members.</summary>
    InvalidTermDefinition,

    /// <summary>A type mapping that is not an IRI, a compact IRI, @id, @vocab, @json or @none.</summary>
    InvalidTypeMapping,

    /// <summary>An @type value that is not a string or an array of strings.</summary>
    InvalidTypeValue,

    /// <summary>A typed value whose @type is not a string or is a blank node.</summary>
    InvalidTypedValue,

    /// <summary>A value object with members it may not have.</summary>
    InvalidValueObject,

    /// <summary>A value object whose @value is an object or an array.</summary>
    InvalidValueObjectValue,

    /// <summary>A vocabulary mapping that is not an IRI, a compact IRI or null.</summary>
    InvalidVocabMapping,

    /// <summary>A term that is an IRI defined as a prefix with a different IRI.</summary>
    IriConfusedWithPrefix,

    /// <summary>A keyword given a definition.</summary>
    KeywordRedefinition,

    /// <summary>A document that could not be loaded.</summary>
    LoadingDocumentFailed,

    /// <summary>A remote context that could not be loaded; with no document loader, every remote context (ADR 0112).</summary>
    LoadingRemoteContextFailed,

    /// <summary>More than one context link header.</summary>
    MultipleContextLinkHeaders,

    /// <summary>A processing mode in conflict with @version.</summary>
    ProcessingModeConflict,

    /// <summary>A protected term redefined.</summary>
    ProtectedTermRedefinition,

    /// <summary>A context including itself.</summary>
    RecursiveContextInclusion,

    /// <summary>A list of lists, which JSON-LD 1.0 forbade.</summary>
    ListOfLists,

    /// <summary>The document is not JSON.</summary>
    InvalidJson,
}

/// <summary>The specification's text for each <see cref="JsonLdErrorCode"/>.</summary>
public static class JsonLdErrorCodes
{
    /// <summary>The specification's spelling of <paramref name="code"/>, as the W3C suite names it.</summary>
    [DesignDecision(typeof(JsonLdOverUtf8Json.ErrorsAreTheSpecificationsCodes), Scope = ExceptionScope.Boundary)]
    public static string Text(JsonLdErrorCode code) => code switch
    {
        JsonLdErrorCode.None => "",
        JsonLdErrorCode.CollidingKeywords => "colliding keywords",
        JsonLdErrorCode.ConflictingIndexes => "conflicting indexes",
        JsonLdErrorCode.ContextOverflow => "context overflow",
        JsonLdErrorCode.CyclicIriMapping => "cyclic IRI mapping",
        JsonLdErrorCode.InvalidIdValue => "invalid @id value",
        JsonLdErrorCode.InvalidImportValue => "invalid @import value",
        JsonLdErrorCode.InvalidIncludedValue => "invalid @included value",
        JsonLdErrorCode.InvalidIndexValue => "invalid @index value",
        JsonLdErrorCode.InvalidNestValue => "invalid @nest value",
        JsonLdErrorCode.InvalidPrefixValue => "invalid @prefix value",
        JsonLdErrorCode.InvalidPropagateValue => "invalid @propagate value",
        JsonLdErrorCode.InvalidProtectedValue => "invalid @protected value",
        JsonLdErrorCode.InvalidReverseValue => "invalid @reverse value",
        JsonLdErrorCode.InvalidVersionValue => "invalid @version value",
        JsonLdErrorCode.InvalidBaseDirection => "invalid base direction",
        JsonLdErrorCode.InvalidBaseIri => "invalid base IRI",
        JsonLdErrorCode.InvalidContainerMapping => "invalid container mapping",
        JsonLdErrorCode.InvalidContextEntry => "invalid context entry",
        JsonLdErrorCode.InvalidContextNullification => "invalid context nullification",
        JsonLdErrorCode.InvalidDefaultLanguage => "invalid default language",
        JsonLdErrorCode.InvalidIriMapping => "invalid IRI mapping",
        JsonLdErrorCode.InvalidJsonLiteral => "invalid JSON literal",
        JsonLdErrorCode.InvalidKeywordAlias => "invalid keyword alias",
        JsonLdErrorCode.InvalidLanguageMapValue => "invalid language map value",
        JsonLdErrorCode.InvalidLanguageMapping => "invalid language mapping",
        JsonLdErrorCode.InvalidLanguageTaggedString => "invalid language-tagged string",
        JsonLdErrorCode.InvalidLanguageTaggedValue => "invalid language-tagged value",
        JsonLdErrorCode.InvalidLocalContext => "invalid local context",
        JsonLdErrorCode.InvalidRemoteContext => "invalid remote context",
        JsonLdErrorCode.InvalidReverseProperty => "invalid reverse property",
        JsonLdErrorCode.InvalidReversePropertyMap => "invalid reverse property map",
        JsonLdErrorCode.InvalidReversePropertyValue => "invalid reverse property value",
        JsonLdErrorCode.InvalidScopedContext => "invalid scoped context",
        JsonLdErrorCode.InvalidSetOrListObject => "invalid set or list object",
        JsonLdErrorCode.InvalidTermDefinition => "invalid term definition",
        JsonLdErrorCode.InvalidTypeMapping => "invalid type mapping",
        JsonLdErrorCode.InvalidTypeValue => "invalid type value",
        JsonLdErrorCode.InvalidTypedValue => "invalid typed value",
        JsonLdErrorCode.InvalidValueObject => "invalid value object",
        JsonLdErrorCode.InvalidValueObjectValue => "invalid value object value",
        JsonLdErrorCode.InvalidVocabMapping => "invalid vocab mapping",
        JsonLdErrorCode.IriConfusedWithPrefix => "IRI confused with prefix",
        JsonLdErrorCode.KeywordRedefinition => "keyword redefinition",
        JsonLdErrorCode.LoadingDocumentFailed => "loading document failed",
        JsonLdErrorCode.LoadingRemoteContextFailed => "loading remote context failed",
        JsonLdErrorCode.MultipleContextLinkHeaders => "multiple context link headers",
        JsonLdErrorCode.ProcessingModeConflict => "processing mode conflict",
        JsonLdErrorCode.ProtectedTermRedefinition => "protected term redefinition",
        JsonLdErrorCode.RecursiveContextInclusion => "recursive context inclusion",
        JsonLdErrorCode.ListOfLists => "list of lists",
        JsonLdErrorCode.InvalidJson => "invalid JSON",
        _ => "unknown error",
    };
}
