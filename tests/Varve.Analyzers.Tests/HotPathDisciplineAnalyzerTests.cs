// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using A = Varve.Analyzers.HotPathDisciplineAnalyzer;

namespace Varve.Analyzers.Tests;

/// <summary>
/// VARVE0003. Inside a <c>[HotPath]</c> member, nothing that allocates, and no
/// call to code that is not held to the same rule (ADR 0064). One violating and
/// one conforming case per finding.
/// </summary>
public class HotPathDisciplineAnalyzerTests
{
    private const string Mark = "[HotPath(typeof(BriefConstraints.AllocationPerQuadIsADefect))]";

    private static Task Clean(string source) =>
        new HotPathTest<A>(source).RunAsync(TestContext.Current.CancellationToken);

    private static Task Reports(string source, string owner, string finding, string decide)
    {
        HotPathTest<A> test = new(source);
        test.ExpectedDiagnostics.Add(new DiagnosticResult(VarveDiagnostics.HotPathDiscipline)
            .WithLocation(0)
            .WithArguments(owner, finding, decide));
        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    private static string F(string format, params object[] arguments) => HotPathTest<A>.Format(format, arguments);

    [Fact]
    public Task A_member_that_is_not_a_hot_path_is_not_checked() => Clean("""
        internal sealed class C
        {
            public object M(int x) => new int[] { x }.Length + "" + x;
        }
        """);

    [Fact]
    public Task Boxing_is_reported() => Reports($$"""
        internal sealed class C
        {
            {{Mark}}
            public object M(int x) => {|#0:x|};
        }
        """, "C.M(int)", F(A.Boxes, "int"), A.BoxesDecide);

    [Fact]
    public Task A_value_kept_typed_is_clean() => Clean($$"""
        internal sealed class C
        {
            {{Mark}}
            public int M(int x) => x;
        }
        """);

    [Fact]
    public Task A_capturing_lambda_is_reported() => Reports($$"""
        internal sealed class C
        {
            {{Mark}}
            public System.Func<int> M(int x) => {|#0:() => x|};
        }
        """, "C.M(int)", F(A.Captures, "lambda", "x"), A.CapturesDecide);

    [Fact]
    public Task A_static_lambda_is_clean() => Clean($$"""
        internal sealed class C
        {
            {{Mark}}
            public System.Func<int, int> M() => static y => y;
        }
        """);

    [Fact]
    public Task A_capturing_local_function_is_reported() => Reports($$"""
        internal sealed class C
        {
            {{Mark}}
            public int M(int x)
            {
                {|#0:int Twice() => x + x;|}
                return Twice();
            }
        }
        """, "C.M(int)", F(A.Captures, "local function", "x"), A.CapturesDecide);

    [Fact]
    public Task A_static_local_function_is_clean() => Clean($$"""
        internal sealed class C
        {
            {{Mark}}
            public int M(int x)
            {
                static int Twice(int y) => y + y;
                return Twice(x);
            }
        }
        """);

    [Fact]
    public Task Allocating_an_array_is_reported() => Reports($$"""
        internal sealed class C
        {
            {{Mark}}
            public int M(int n) => {|#0:new byte[n]|}.Length;
        }
        """, "C.M(int)", F(A.AllocatesArray, "byte"), A.AllocatesArrayDecide);

    [Fact]
    public Task A_stackalloc_span_is_clean() => Clean($$"""
        internal sealed class C
        {
            {{Mark}}
            public int M()
            {
                System.Span<byte> buffer = stackalloc byte[16];
                return buffer.Length;
            }
        }
        """);

    [Fact]
    public Task Allocating_a_reference_type_is_reported() => Reports($$"""
        internal sealed class Box { }

        internal sealed class C
        {
            {{Mark}}
            public Box M() => {|#0:new Box()|};
        }
        """, "C.M()", F(A.AllocatesObject, "Box"), A.AllocatesObjectDecide);

    [Fact]
    public Task Creating_a_hot_path_struct_is_clean() => Clean($$"""
        {{Mark}}
        internal readonly struct Handle
        {
            public Handle(ulong value) => Value = value;
            public ulong Value { get; }
        }

        internal sealed class C
        {
            {{Mark}}
            public ulong M(ulong v) => new Handle(v).Value;
        }
        """);

    [Fact]
    public Task A_collection_expression_building_an_array_is_reported() => Reports($$"""
        internal sealed class C
        {
            {{Mark}}
            public int[] M(int x) => {|#0:[x, x]|};
        }
        """, "C.M(int)", F(A.AllocatesArray, "int"), A.AllocatesArrayDecide);

    [Fact]
    public Task A_collection_expression_into_a_span_is_clean() => Clean($$"""
        internal sealed class C
        {
            {{Mark}}
            public int M(int x)
            {
                System.ReadOnlySpan<int> pair = [x, x];
                return pair.Length;
            }
        }
        """);

    [Fact]
    public Task String_concatenation_is_reported() => Reports($$"""
        internal sealed class C
        {
            {{Mark}}
            public string M(string a, string b) => {|#0:a + b|};
        }
        """, "C.M(string, string)", A.BuildsString, A.BuildsStringDecide);

    [Fact]
    public Task String_interpolation_is_reported() => Reports($$"""
        internal sealed class C
        {
            {{Mark}}
            public string M(string a) => {|#0:$"<{a}>"|};
        }
        """, "C.M(string)", A.BuildsString, A.BuildsStringDecide);

    [Fact]
    public Task A_constant_string_is_clean() => Clean($$"""
        internal sealed class C
        {
            {{Mark}}
            public string M() => "a" + "b";
        }
        """);

    [Fact]
    public Task A_params_call_is_reported() => Reports($$"""
        internal sealed class C
        {
            {{Mark}}
            public static int Sum(params int[] values) => values.Length;

            {{Mark}}
            public int M(int x) => {|#0:Sum(x, x)|};
        }
        """, "C.M(int)", F(A.ParamsCall, "C.Sum(params int[])"), A.ParamsCallDecide);

    [Fact]
    public Task A_params_span_call_is_clean() => Clean($$"""
        internal sealed class C
        {
            {{Mark}}
            public static int Sum(params System.ReadOnlySpan<int> values) => values.Length;

            {{Mark}}
            public int M(int x) => Sum(x, x);
        }
        """);

    [Fact]
    public Task Linq_is_reported() => Reports($$"""
        using System.Linq;

        internal sealed class C
        {
            {{Mark}}
            public int M(int[] values) => {|#0:values.Count()|};
        }
        """, "C.M(int[])", F(A.UsesLinq, "Count"), A.UsesLinqDecide);

    [Fact]
    public Task Foreach_over_a_class_enumerator_is_reported() => Reports($$"""
        internal sealed class C
        {
            {{Mark}}
            public int M(System.Collections.Generic.IEnumerable<int> values)
            {
                int sum = 0;
                foreach (int v in {|#0:values|})
                {
                    sum += v;
                }

                return sum;
            }
        }
        """, "C.M(IEnumerable<int>)", F(A.ClassEnumerator, "System.Collections.Generic.IEnumerable<int>"), A.ClassEnumeratorDecide);

    [Fact]
    public Task Foreach_over_a_span_or_an_array_is_clean() => Clean($$"""
        internal sealed class C
        {
            {{Mark}}
            public int M(System.ReadOnlySpan<int> span, int[] array)
            {
                int sum = 0;
                foreach (int v in span)
                {
                    sum += v;
                }

                foreach (int v in array)
                {
                    sum += v;
                }

                return sum;
            }
        }
        """);

    [Fact]
    public Task An_async_hot_path_is_reported() => Reports($$"""
        internal sealed class C
        {
            {{Mark}}
            public async System.Threading.Tasks.ValueTask {|#0:M|}() => await default(System.Threading.Tasks.ValueTask);
        }
        """, "C.M()", A.IsAsync, A.IsAsyncDecide);

    [Fact]
    public Task Calling_a_Varve_member_that_is_not_a_hot_path_is_reported() => Reports($$"""
        internal static class Helper
        {
            public static int Twice(int x) => x + x;
        }

        internal sealed class C
        {
            {{Mark}}
            public int M(int x) => {|#0:Helper.Twice(x)|};
        }
        """, "C.M(int)", F(A.CallsColdMember, "Helper.Twice(int)"), F(A.CallsColdMemberDecide, "Helper.Twice(int)"));

    [Fact]
    public Task Calling_a_hot_path_member_is_clean() => Clean($$"""
        internal static class Helper
        {
            {{Mark}}
            public static int Twice(int x) => x + x;
        }

        internal sealed class C
        {
            {{Mark}}
            public int M(int x) => Helper.Twice(x);
        }
        """);

    [Fact]
    public Task Every_member_of_a_hot_path_type_is_held_to_the_rule_and_may_call_the_others() => Reports($$"""
        {{Mark}}
        internal sealed class C
        {
            private int Twice(int x) => x + x;

            public int M(int x) => Twice(x);

            public object N(int x) => {|#0:x|};
        }
        """, "C.N(int)", F(A.Boxes, "int"), A.BoxesDecide);

    [Fact]
    public Task Calling_an_allow_listed_BCL_member_is_clean() => Clean($$"""
        internal sealed class C
        {
            {{Mark}}
            public ushort M(System.ReadOnlySpan<byte> bytes) =>
                System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(0, bytes.Length));
        }
        """);

    [Fact]
    public Task Calling_a_BCL_member_that_is_not_on_the_allow_list_is_reported() => Reports($$"""
        internal sealed class C
        {
            {{Mark}}
            public int M(int a, int b) => {|#0:System.Math.Max(a, b)|};
        }
        """, "C.M(int, int)", F(A.CallsColdMember, "Math.Max(int, int)"), F(A.CallsColdMemberDecide, "Math.Max(int, int)"));

    [Fact]
    public Task A_conversion_operator_of_an_allow_listed_type_is_clean() =>
        new HotPathTest<A>($$"""
            internal sealed class C
            {
                {{Mark}}
                public System.Index M(int x) => x;
            }
            """, "System.Index").RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_conversion_operator_of_a_type_not_on_the_allow_list_is_reported() => Reports($$"""
        internal sealed class C
        {
            {{Mark}}
            public System.Index M(int x) => {|#0:x|};
        }
        """, "C.M(int)", F(A.CallsColdMember, "Index.implicit operator Index(int)"), F(A.CallsColdMemberDecide, "Index.implicit operator Index(int)"));

    [Fact]
    public Task An_array_length_is_an_instruction_and_not_a_call() => Clean($$"""
        internal sealed class C
        {
            {{Mark}}
            public int M(int[] values) => values.Length;
        }
        """);

    [Fact]
    public Task What_is_built_only_to_be_thrown_is_not_reported() => Clean($$"""
        internal sealed class C
        {
            {{Mark}}
            public int M(int x) => x >= 0 ? x : throw new System.ArgumentOutOfRangeException(nameof(x), "negative: " + x);
        }
        """);

    [Fact]
    public Task A_member_carrying_a_design_decision_is_not_reported() => Clean($$"""
        internal sealed class C
        {
            {{Mark}}
            [DesignDecision(typeof(BriefConstraints.AllocationPerQuadIsADefect), Scope = ExceptionScope.HotPath)]
            public object M(int x) => x;
        }
        """);

    [Fact]
    public Task A_callee_declared_safe_for_hot_paths_is_clean() => Clean($$"""
        internal static class Helper
        {
            [DesignDecision(typeof(BriefConstraints.AllocationPerQuadIsADefect), Scope = ExceptionScope.HotPath)]
            public static int Twice(int x) => x + x;
        }

        internal sealed class C
        {
            {{Mark}}
            public int M(int x) => Helper.Twice(x);
        }
        """);
}
