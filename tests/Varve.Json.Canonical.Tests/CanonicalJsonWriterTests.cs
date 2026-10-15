// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Text;
using Xunit;

namespace Varve.Json.Canonical.Tests;

public sealed class CanonicalJsonWriterTests
{
    [Fact]
    public void writes_nested_values_with_no_whitespace()
    {
        ArrayBufferWriter<byte> output = new();
        CanonicalJsonWriter writer = new(output);
        writer.StartObject();
        writer.WriteName("a");
        writer.StartArray();
        writer.WriteNumber(1);
        writer.WriteBoolean(true);
        writer.WriteNull();
        writer.WriteString("x");
        writer.WriteCanonicalValue("{\"k\":2}"u8);
        writer.EndArray();
        writer.WriteName("b");
        writer.WriteNumber(-0.0);
        writer.EndObject();

        Assert.True(writer.IsComplete);
        Assert.Equal("{\"a\":[1,true,null,\"x\",{\"k\":2}],\"b\":0}", Encoding.UTF8.GetString(output.WrittenSpan));
    }

    [Fact]
    public void a_member_out_of_order_is_refused()
    {
        CanonicalJsonWriter writer = new(new ArrayBufferWriter<byte>());
        writer.StartObject();
        writer.WriteName("b");
        writer.WriteNumber(1);
        Assert.Throws<InvalidOperationException>(() => writer.WriteName("a"));
    }

    [Fact]
    public void a_repeated_member_is_refused()
    {
        CanonicalJsonWriter writer = new(new ArrayBufferWriter<byte>());
        writer.StartObject();
        writer.WriteName("a");
        writer.WriteNumber(1);
        Assert.Throws<InvalidOperationException>(() => writer.WriteName("a"));
    }

    [Fact]
    public void a_value_without_a_name_is_refused()
    {
        CanonicalJsonWriter writer = new(new ArrayBufferWriter<byte>());
        writer.StartObject();
        Assert.Throws<InvalidOperationException>(() => writer.WriteNumber(1));
    }

    [Fact]
    public void a_second_top_level_value_is_refused()
    {
        CanonicalJsonWriter writer = new(new ArrayBufferWriter<byte>());
        writer.WriteNumber(1);
        Assert.Throws<InvalidOperationException>(() => writer.WriteNumber(2));
    }

    [Fact]
    public void a_non_finite_number_is_refused()
    {
        CanonicalJsonWriter writer = new(new ArrayBufferWriter<byte>());
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteNumber(double.NaN));
    }
}
