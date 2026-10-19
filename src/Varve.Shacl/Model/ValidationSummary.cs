// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Shacl.Model;

/// <summary>
/// How many results a validation produced, by severity — read without
/// building the report graph (ADR 0154).
/// </summary>
public readonly struct ValidationSummary : IEquatable<ValidationSummary>
{
    internal ValidationSummary(int violations, int warnings, int infos, int traces, int debugs, int others)
    {
        Violations = violations;
        Warnings = warnings;
        Infos = infos;
        Traces = traces;
        Debugs = debugs;
        Others = others;
    }

    /// <summary>Results with severity <c>sh:Violation</c>.</summary>
    [DesignDecision(typeof(AMinimalPublicShaclApi.ShaclModelPrimitives), Scope = ExceptionScope.Boundary)]
    public int Violations { get; }

    /// <summary>Results with severity <c>sh:Warning</c>.</summary>
    [DesignDecision(typeof(AMinimalPublicShaclApi.ShaclModelPrimitives), Scope = ExceptionScope.Boundary)]
    public int Warnings { get; }

    /// <summary>Results with severity <c>sh:Info</c>.</summary>
    [DesignDecision(typeof(AMinimalPublicShaclApi.ShaclModelPrimitives), Scope = ExceptionScope.Boundary)]
    public int Infos { get; }

    /// <summary>Results with severity <c>sh:Trace</c>.</summary>
    [DesignDecision(typeof(AMinimalPublicShaclApi.ShaclModelPrimitives), Scope = ExceptionScope.Boundary)]
    public int Traces { get; }

    /// <summary>Results with severity <c>sh:Debug</c>.</summary>
    [DesignDecision(typeof(AMinimalPublicShaclApi.ShaclModelPrimitives), Scope = ExceptionScope.Boundary)]
    public int Debugs { get; }

    /// <summary>Results with any other severity IRI.</summary>
    [DesignDecision(typeof(AMinimalPublicShaclApi.ShaclModelPrimitives), Scope = ExceptionScope.Boundary)]
    public int Others { get; }

    /// <summary>Every result.</summary>
    [DesignDecision(typeof(AMinimalPublicShaclApi.ShaclModelPrimitives), Scope = ExceptionScope.Boundary)]
    public int Total => Violations + Warnings + Infos + Traces + Debugs + Others;

    /// <inheritdoc/>
    public bool Equals(ValidationSummary other) =>
        Violations == other.Violations && Warnings == other.Warnings && Infos == other.Infos
        && Traces == other.Traces && Debugs == other.Debugs && Others == other.Others;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ValidationSummary other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Violations, Warnings, Infos, Traces, Debugs, Others);

    /// <summary>Equality.</summary>
    public static bool operator ==(ValidationSummary left, ValidationSummary right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(ValidationSummary left, ValidationSummary right) => !left.Equals(right);
}
