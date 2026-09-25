// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;

namespace Varve.Sparql.Results;

/// <summary>
/// Writes SPARQL results — a variable-binding table or a boolean — in one of
/// the four formats, as UTF-8 (<c>sparql-results.md</c> §5).
/// </summary>
/// <remarks>
/// <para>
/// The call sequence is <see cref="WriteHead"/>, then for each solution
/// <see cref="StartSolution"/>, bindings in ascending variable order, and
/// <see cref="EndSolution"/>, then <see cref="WriteEnd"/>; or
/// <see cref="WriteBoolean"/> and <see cref="WriteEnd"/>. A call out of that
/// order throws <see cref="InvalidOperationException"/>. A variable given no
/// binding in a solution is unbound.
/// </para>
/// <para>
/// Over a <see cref="Stream"/> the writer buffers, and writes to the stream
/// only in <see cref="Flush"/> or <see cref="FlushAsync"/>;
/// <see cref="BytesPending"/> says how much is waiting. Over an
/// <see cref="IBufferWriter{T}"/> bytes are written as the calls are made.
/// The writer allocates nothing per solution.
/// </para>
/// </remarks>
public sealed class SparqlResultsWriter : IDisposable
{
    private readonly ResultsOutput _output;
    private readonly FormatWriter _format;
    private State _state;
    private int _variableCount;
    private int _next;

    /// <summary>A writer into a buffer writer, which receives every byte as it is written.</summary>
    public SparqlResultsWriter(IBufferWriter<byte> output, SparqlResultsFormat format)
        : this(new ResultsOutput(output ?? throw new ArgumentNullException(nameof(output))), format)
    {
    }

    /// <summary>
    /// A writer into a stream, through a pooled buffer that
    /// <see cref="Flush"/> or <see cref="FlushAsync"/> writes out.
    /// </summary>
    public SparqlResultsWriter(Stream output, SparqlResultsFormat format)
        : this(new ResultsOutput(output ?? throw new ArgumentNullException(nameof(output))), format)
    {
    }

    private SparqlResultsWriter(ResultsOutput output, SparqlResultsFormat format)
    {
        _output = output;
        Format = format;
        _format = format switch
        {
            SparqlResultsFormat.Xml => new XmlResultsWriter(output),
            SparqlResultsFormat.Json => new JsonResultsWriter(output),
            SparqlResultsFormat.Csv => new CsvResultsWriter(output),
            SparqlResultsFormat.Tsv => new TsvResultsWriter(output),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    private enum State
    {
        Start,
        Head,
        Solution,
        Boolean,
        End,
    }

    /// <summary>The format being written.</summary>
    public SparqlResultsFormat Format { get; }

    /// <summary>Bytes buffered for a stream and not yet written to it; zero over a buffer writer.</summary>
    public int BytesPending => _output.Pending;

    /// <summary>Writes the head of a variable-binding result.</summary>
    /// <param name="variables">The variables, by name without <c>?</c>, in column order.</param>
    public void WriteHead(IReadOnlyList<string> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        Expect(State.Start, nameof(WriteHead));

        foreach (string variable in variables)
        {
            if (string.IsNullOrEmpty(variable))
            {
                throw new ArgumentException("A variable has no name.", nameof(variables));
            }
        }

        _format.WriteHead(variables);
        _variableCount = variables.Count;
        _state = State.Head;
    }

    /// <summary>Writes a boolean result: the head and the value. <see cref="WriteEnd"/> follows.</summary>
    public void WriteBoolean(bool value)
    {
        Expect(State.Start, nameof(WriteBoolean));
        _format.WriteBoolean(value);
        _state = State.Boolean;
    }

    /// <summary>Starts a solution.</summary>
    public void StartSolution()
    {
        Expect(State.Head, nameof(StartSolution));
        _format.StartSolution();
        _next = 0;
        _state = State.Solution;
    }

    /// <summary>Binds the variable at <paramref name="variable"/> in the current solution.</summary>
    public void WriteBinding(int variable, RdfTerm term)
    {
        ArgumentNullException.ThrowIfNull(term);
        Bind(variable);
        _format.WriteBinding(variable, new TermInput(term));
    }

    /// <summary>Binds the variable at <paramref name="variable"/> to a reader's term, without materialising it.</summary>
    public void WriteBinding(int variable, RdfTermView term)
    {
        Bind(variable);
        _format.WriteBinding(variable, new TermInput(term));
    }

    /// <summary>Ends the current solution.</summary>
    public void EndSolution()
    {
        Expect(State.Solution, nameof(EndSolution));
        _format.EndSolution();
        _state = State.Head;
    }

    /// <summary>Ends the document.</summary>
    public void WriteEnd()
    {
        if (_state is not (State.Head or State.Boolean))
        {
            throw new InvalidOperationException(
                "WriteEnd follows the head and every solution's EndSolution, or WriteBoolean.");
        }

        _format.WriteEnd(isBoolean: _state == State.Boolean);
        _state = State.End;
    }

    /// <summary>Writes what is buffered to the stream. Nothing to do over a buffer writer.</summary>
    public void Flush() => _output.Flush();

    /// <summary>Writes what is buffered to the stream, asynchronously.</summary>
    public ValueTask FlushAsync(CancellationToken cancellationToken = default) => _output.FlushAsync(cancellationToken);

    /// <summary>Returns the pooled buffer. Does not flush: a stream is written only by a flush.</summary>
    public void Dispose() => _output.Dispose();

    private void Bind(int variable)
    {
        Expect(State.Solution, nameof(WriteBinding));

        if ((uint)variable >= (uint)_variableCount)
        {
            throw new ArgumentOutOfRangeException(nameof(variable), variable, "No such variable in the head.");
        }

        if (variable < _next)
        {
            throw new InvalidOperationException(
                "Bindings of a solution are written once each and in ascending variable order.");
        }

        _next = variable + 1;
    }

    private void Expect(State state, string call)
    {
        if (_state != state)
        {
            throw new InvalidOperationException(call + " is not valid here: the writer is at " + _state + ".");
        }
    }
}
