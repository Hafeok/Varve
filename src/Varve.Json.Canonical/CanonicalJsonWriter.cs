// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Json.Canonical;

/// <summary>
/// A forward-only writer of one canonical JSON document into a buffer. It adds
/// no whitespace, writes strings and numbers through <see cref="CanonicalJson"/>,
/// and refuses what RFC 8785 would not produce: a member whose name does not
/// sort after the one before it, a non-finite number, a value where a name is
/// due, or a second top-level value.
/// </summary>
[Contract(typeof(VarveJsonCanonical.CanonicalJsonAtLayerZero), Role = "a writer that can only write canonical JSON")]
public sealed class CanonicalJsonWriter
{
    private readonly IBufferWriter<byte> _output;
    private readonly List<Frame> _frames = [];

    /// <summary>A writer into <paramref name="output"/>.</summary>
    public CanonicalJsonWriter(IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
    }

    /// <summary>Whether one complete top-level value has been written.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Opens an object.</summary>
    public void StartObject()
    {
        BeginValue();
        Put((byte)'{');
        _frames.Add(new Frame(isObject: true));
    }

    /// <summary>Closes the innermost object.</summary>
    public void EndObject()
    {
        Frame frame = Top(isObject: true);

        if (frame.ExpectsValue)
        {
            throw new InvalidOperationException("A member has a name and no value.");
        }

        _frames.RemoveAt(_frames.Count - 1);
        Put((byte)'}');
        EndValue();
    }

    /// <summary>Opens an array.</summary>
    public void StartArray()
    {
        BeginValue();
        Put((byte)'[');
        _frames.Add(new Frame(isObject: false));
    }

    /// <summary>Closes the innermost array.</summary>
    public void EndArray()
    {
        Top(isObject: false);
        _frames.RemoveAt(_frames.Count - 1);
        Put((byte)']');
        EndValue();
    }

    /// <summary>Writes a member's name, which must sort after the previous member's.</summary>
    [DesignDecision(typeof(VarveJsonCanonical.JsonValuesAreBclPrimitives), Scope = ExceptionScope.Boundary)]
    public void WriteName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        WriteName(Encoding.UTF8.GetBytes(name));
    }

    /// <summary>Writes a member's name, UTF-8 encoded, which must sort after the previous member's.</summary>
    public void WriteName(ReadOnlySpan<byte> utf8Name)
    {
        Frame frame = Top(isObject: true);

        if (frame.ExpectsValue)
        {
            throw new InvalidOperationException("A member has a name and no value.");
        }

        if (frame.LastName is { } last && CanonicalJson.CompareNames(last, utf8Name) >= 0)
        {
            throw new InvalidOperationException(
                "Member \"" + Encoding.UTF8.GetString(utf8Name) + "\" does not sort after \"" + Encoding.UTF8.GetString(last) + "\" (RFC 8785 §3.2.3).");
        }

        if (frame.Count > 0)
        {
            Put((byte)',');
        }

        CanonicalJson.WriteString(_output, utf8Name);
        Put((byte)':');
        frame.LastName = utf8Name.ToArray();
        frame.ExpectsValue = true;
    }

    /// <summary>Writes a string value.</summary>
    [DesignDecision(typeof(VarveJsonCanonical.JsonValuesAreBclPrimitives), Scope = ExceptionScope.Boundary)]
    public void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteString(Encoding.UTF8.GetBytes(value));
    }

    /// <summary>Writes a string value, UTF-8 encoded.</summary>
    public void WriteString(ReadOnlySpan<byte> utf8)
    {
        BeginValue();
        CanonicalJson.WriteString(_output, utf8);
        EndValue();
    }

    /// <summary>Writes a finite number in ECMAScript form.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is NaN or infinite.</exception>
    [DesignDecision(typeof(VarveJsonCanonical.JsonValuesAreBclPrimitives), Scope = ExceptionScope.Boundary)]
    public void WriteNumber(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "JSON has no NaN or infinity (RFC 8785 §3.2.2.3).");
        }

        BeginValue();
        CanonicalJson.WriteNumber(_output, value);
        EndValue();
    }

    /// <summary>Writes <c>true</c> or <c>false</c>.</summary>
    [DesignDecision(typeof(VarveJsonCanonical.JsonValuesAreBclPrimitives), Scope = ExceptionScope.Boundary)]
    public void WriteBoolean(bool value)
    {
        BeginValue();
        Put(value ? "true"u8 : "false"u8);
        EndValue();
    }

    /// <summary>Writes <c>null</c>.</summary>
    public void WriteNull()
    {
        BeginValue();
        Put("null"u8);
        EndValue();
    }

    /// <summary>
    /// Writes one value that is already canonical JSON — a document another
    /// canonical writer produced — as it is. The caller vouches for its form.
    /// </summary>
    public void WriteCanonicalValue(ReadOnlySpan<byte> canonical)
    {
        if (canonical.IsEmpty)
        {
            throw new ArgumentException("A JSON value is not empty.", nameof(canonical));
        }

        BeginValue();
        Put(canonical);
        EndValue();
    }

    private void BeginValue()
    {
        if (IsComplete)
        {
            throw new InvalidOperationException("A canonical JSON document holds one top-level value.");
        }

        if (_frames.Count == 0)
        {
            return;
        }

        Frame frame = _frames[^1];

        if (frame.IsObject)
        {
            if (!frame.ExpectsValue)
            {
                throw new InvalidOperationException("An object's value needs a name first.");
            }
        }
        else if (frame.Count > 0)
        {
            Put((byte)',');
        }
    }

    private void EndValue()
    {
        if (_frames.Count == 0)
        {
            IsComplete = true;
            return;
        }

        Frame frame = _frames[^1];
        frame.ExpectsValue = false;
        frame.Count++;
    }

    private Frame Top(bool isObject)
    {
        if (_frames.Count == 0 || _frames[^1].IsObject != isObject)
        {
            throw new InvalidOperationException(isObject ? "No object is open." : "No array is open.");
        }

        return _frames[^1];
    }

    private void Put(byte b)
    {
        _output.GetSpan(1)[0] = b;
        _output.Advance(1);
    }

    private void Put(ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(_output.GetSpan(bytes.Length));
        _output.Advance(bytes.Length);
    }

    private sealed class Frame(bool isObject)
    {
        public bool IsObject { get; } = isObject;

        public int Count { get; set; }

        public bool ExpectsValue { get; set; }

        public byte[]? LastName { get; set; }
    }
}
