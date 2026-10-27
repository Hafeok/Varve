// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Varve.Rdf;
using Varve.Sparql.Entailment;
using Varve.Sparql.Evaluation;

namespace Varve.Protocol.Endpoints;

/// <summary>
/// The two functions a rewritten query calls under D (ADR 0193): a literal's
/// canonical form and its value's types, registered beside the host's own
/// functions once per host's options, which hold the result.
/// </summary>
internal static class EntailmentFunctions
{
    private static readonly RdfTerm Boolean = RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#boolean"u8);
    /// <summary><paramref name="host"/> with the two functions registered; the host's own take precedence by IRI.</summary>
    internal static EvaluationOptions With(EvaluationOptions host)
    {
        Dictionary<string, IExtensionFunction> functions = new(StringComparer.Ordinal)
        {
            [Encoding.UTF8.GetString(LiteralValues.FunctionIri.Lexical)] = new Canonical(),
            [Encoding.UTF8.GetString(LiteralValues.HasValueTypeIri.Lexical)] = new HasValueType(),
        };

        foreach ((string iri, IExtensionFunction function) in host.Functions ?? new Dictionary<string, IExtensionFunction>())
        {
            functions[iri] = function;
        }

        return new EvaluationOptions
        {
            Optimise = host.Optimise,
            ValueAccess = host.ValueAccess,
            ImplicitTimezoneOffset = host.ImplicitTimezoneOffset,
            Clock = host.Clock,
            Randomness = host.Randomness,
            ServiceHandler = host.ServiceHandler,
            Functions = functions,
            Aggregates = host.Aggregates,
            RegexTimeout = host.RegexTimeout,
            MemoryBudget = host.MemoryBudget,
        };
    }

    private sealed class Canonical : IExtensionFunction
    {
        public bool TryEvaluate(ReadOnlySpan<RdfTerm> arguments, [NotNullWhen(true)] out RdfTerm? result)
        {
            result = arguments.Length == 1 ? LiteralValues.Canonical(arguments[0]) : null;
            return result is not null;
        }
    }

    private sealed class HasValueType : IExtensionFunction
    {
        public bool TryEvaluate(ReadOnlySpan<RdfTerm> arguments, [NotNullWhen(true)] out RdfTerm? result)
        {
            result = arguments.Length == 2 ? RdfTerm.Literal(LiteralValues.HasValueType(arguments[0], arguments[1]) ? "true"u8 : "false"u8, Boolean) : null;
            return result is not null;
        }
    }
}
