// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text;

namespace Varve.Benchmarks;

/// <summary>
/// The document every benchmark reads, generated so that the numbers can be
/// reproduced without shipping a file.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not a "nice" document. One in eight objects is a literal
/// carrying escapes, one in eight subjects is a blank node, and the language
/// tags vary, because a parser that is fast only on IRIs is fast on the easy
/// half of the problem. It is still a synthetic document and the numbers
/// should be read as such: a real dataset has different term-length
/// distribution, different escape density and a different IRI prefix profile.
/// </para>
/// </remarks>
internal static class Dataset
{
    internal const int Quads = 100_000;

    internal static byte[] Utf8 { get; } = Build();

    internal static string Text { get; } = Encoding.UTF8.GetString(Utf8);

    private static byte[] Build()
    {
        StringBuilder builder = new(Quads * 110);

        for (int i = 0; i < Quads; i++)
        {
            if (i % 8 == 3)
            {
                builder.Append("_:node").Append(i.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append("<http://example.org/dataset/subject/")
                    .Append(i.ToString(CultureInfo.InvariantCulture)).Append('>');
            }

            builder.Append(" <http://example.org/dataset/predicate/")
                .Append((i % 17).ToString(CultureInfo.InvariantCulture)).Append("> ");

            switch (i % 8)
            {
                case 0:
                    builder.Append("<http://example.org/dataset/object/")
                        .Append(i.ToString(CultureInfo.InvariantCulture)).Append('>');
                    break;

                case 1:
                    builder.Append('"').Append("plain value ")
                        .Append(i.ToString(CultureInfo.InvariantCulture)).Append('"');
                    break;

                case 2:
                    builder.Append('"').Append(i.ToString(CultureInfo.InvariantCulture))
                        .Append("\"^^<http://www.w3.org/2001/XMLSchema#integer>");
                    break;

                case 4:
                    builder.Append("\"tagged ").Append(i.ToString(CultureInfo.InvariantCulture))
                        .Append("\"@").Append(i % 2 == 0 ? "en-GB" : "de");
                    break;

                case 5:
                    builder.Append("\"escaped \\u00E9 \\\"quoted\\\" \\n line ")
                        .Append(i.ToString(CultureInfo.InvariantCulture)).Append('"');
                    break;

                default:
                    builder.Append("<http://example.org/dataset/object/")
                        .Append((i % 1000).ToString(CultureInfo.InvariantCulture)).Append('>');
                    break;
            }

            builder.Append(" <http://example.org/dataset/graph/")
                .Append((i % 5).ToString(CultureInfo.InvariantCulture)).Append("> .\n");
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}
