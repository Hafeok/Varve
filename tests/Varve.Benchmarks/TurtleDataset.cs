using System.Globalization;
using System.Text;

namespace Varve.Benchmarks;

/// <summary>
/// A Turtle document that exercises what Turtle adds over N-Quads.
/// </summary>
/// <remarks>
/// Every statement carries a prefixed name to expand, a predicate-object list,
/// an object list, a blank node property list and a collection, so the
/// measurement is of Turtle's own work rather than of line splitting. Ten
/// thousand statements, which is 90,000 quads — close enough to the N-Quads
/// dataset's 100,000 that the two numbers can be read beside each other.
/// </remarks>
internal static class TurtleDataset
{
    internal const int Statements = 10_000;

    internal static byte[] Utf8 { get; } = Build();

    internal static string Text { get; } = Encoding.UTF8.GetString(Utf8);

    private static byte[] Build()
    {
        StringBuilder builder = new(Statements * 200);
        builder.Append("@prefix p: <http://example.org/> .\n");

        for (int i = 0; i < Statements; i++)
        {
            string n = i.ToString(CultureInfo.InvariantCulture);

            builder.Append("p:s").Append(n)
                .Append(" p:p \"value ").Append(n).Append(" with an escape \\u00E9\"@en , 1.5e3 , true ;\n")
                .Append("  p:q [ p:r ( <rel").Append(n).Append("> p:t ) ] ;\n")
                .Append("  a p:C .\n");
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}
