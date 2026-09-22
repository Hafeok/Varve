using System.Globalization;
using System.Text;

namespace Varve.Benchmarks;

/// <summary>
/// A Turtle document that exercises what Turtle adds over N-Quads.
/// </summary>
/// <remarks>
/// Every statement carries a prefixed name to expand, a predicate-object list,
/// an object list, a blank node property list and a collection, so the
/// measurement is of Turtle's own work rather than of line splitting.
/// <para>
/// Each statement yields ten quads: three from the object list, one naming the
/// blank node property list, one inside it, four from the two-item collection's
/// <c>rdf:first</c>/<c>rdf:rest</c> pairs, and one from <c>a</c>. Ten thousand
/// statements is therefore 100,000 quads, the same count as the N-Quads
/// dataset, so the two sets of numbers can be read beside each other.
/// </para>
/// </remarks>
internal static class TurtleDataset
{
    internal const int Statements = 10_000;

    /// <summary>Ten per statement; see the remarks on this type.</summary>
    internal const int Quads = Statements * 10;

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
