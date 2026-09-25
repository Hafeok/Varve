// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Sparql.Results;

/// <summary>The four SPARQL result formats.</summary>
public enum SparqlResultsFormat : byte
{
    /// <summary>SPARQL Query Results XML Format, <c>application/sparql-results+xml</c>.</summary>
    Xml,

    /// <summary>SPARQL 1.1 Query Results JSON Format, <c>application/sparql-results+json</c>.</summary>
    Json,

    /// <summary>SPARQL 1.1 Query Results CSV Format. Lossy: terms are written as bare text.</summary>
    Csv,

    /// <summary>SPARQL 1.1 Query Results TSV Format. Terms in Turtle syntax.</summary>
    Tsv,
}
