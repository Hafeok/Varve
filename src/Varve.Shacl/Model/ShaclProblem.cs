// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Shacl.Model;

/// <summary>Why a shapes graph was refused or a validation failed (ADR 0151).</summary>
public enum ShaclProblemKind
{
    /// <summary>A shape breaks a syntax rule (SHACL 1.2 Core Appendix A, or the component's own).</summary>
    IllFormedShape = 1,

    /// <summary>A cycle of shapes through a negative or target reference (ADR 0150).</summary>
    UnstratifiableRecursion = 2,

    /// <summary>A shape needs an extension the options do not carry, such as SHACL-SPARQL (ADR 0148).</summary>
    MissingExtension = 3,

    /// <summary><c>sh:entailment</c> names a regime other than simple entailment (§1.4).</summary>
    UnsupportedEntailment = 4,

    /// <summary>A pattern or its flags are not an XPath regular expression (ADR 0153).</summary>
    InvalidPattern = 5,

    /// <summary>A pattern took longer than <c>ShaclOptions.RegexTimeout</c> on one value.</summary>
    PatternTimeout = 6,

    /// <summary>An extension's constraint reported a failure (§6.6.1).</summary>
    ExtensionFailure = 7,
}

/// <summary>
/// One reason a shapes graph was refused or a validation failed: what kind,
/// where — the shape, and the focus node when there was one — and what, for a
/// person. Never a validation result (ADR 0151).
/// </summary>
public sealed class ShaclProblem
{
    /// <summary>A problem.</summary>
    public ShaclProblem(ShaclProblemKind kind, RdfTerm? shape, RdfTerm? focusNode, string message, IReadOnlyList<RdfTerm>? cycle = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        Kind = kind;
        Shape = shape;
        FocusNode = focusNode;
        Message = message;
        Cycle = cycle ?? [];
    }

    /// <summary>The kind.</summary>
    public ShaclProblemKind Kind { get; }

    /// <summary>The shape it concerns, when it concerns one.</summary>
    public RdfTerm? Shape { get; }

    /// <summary>The focus node being validated, for a failure during validation.</summary>
    public RdfTerm? FocusNode { get; }

    /// <summary>What is wrong, naming the rule and its section.</summary>
    [DesignDecision(typeof(AMinimalPublicShaclApi.ShaclModelPrimitives), Scope = ExceptionScope.Boundary)]
    public string Message { get; }

    /// <summary>
    /// For <see cref="ShaclProblemKind.UnstratifiableRecursion"/>: the cycle,
    /// as shape, parameter, shape, parameter, …, ending at the shape it
    /// started from. Empty otherwise.
    /// </summary>
    public IReadOnlyList<RdfTerm> Cycle { get; }

    /// <inheritdoc/>
    public override string ToString() => Kind + ": " + Message;
}
