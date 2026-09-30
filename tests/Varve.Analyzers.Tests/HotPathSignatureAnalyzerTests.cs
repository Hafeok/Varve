// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using A = Varve.Analyzers.HotPathSignatureAnalyzer;

namespace Varve.Analyzers.Tests;

/// <summary>
/// VARVE0004. A <c>[HotPath]</c> member's signature does not commit its callers
/// to an allocation or to interface dispatch (ADR 0064). One violating and one
/// conforming case per finding.
/// </summary>
public class HotPathSignatureAnalyzerTests
{
    private const string Mark = "[HotPath(typeof(BriefConstraints.AllocationPerQuadIsADefect))]";

    private static Task Clean(string source) =>
        new HotPathTest<A>(source).RunAsync(TestContext.Current.CancellationToken);

    private static Task Reports(string source, string owner, string finding, string decide)
    {
        HotPathTest<A> test = new(source);
        test.ExpectedDiagnostics.Add(new DiagnosticResult(VarveDiagnostics.HotPathSignature)
            .WithLocation(0)
            .WithArguments(owner, finding, decide));
        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    private static string F(string format, params object[] arguments) => HotPathTest<A>.Format(format, arguments);

    [Fact]
    public Task A_member_that_is_not_a_hot_path_is_not_checked() => Clean("""
        internal sealed class C
        {
            public System.Collections.Generic.IEnumerable<int> M(System.IComparable c) => new int[0];
        }
        """);

    [Fact]
    public Task Returning_a_sequence_is_reported() => Reports($$"""
        internal sealed class C
        {
            {{Mark}}
            public System.Collections.Generic.IEnumerable<int> {|#0:M|}() => System.Array.Empty<int>();
        }
        """, "C.M()", F(A.ReturnsSequence, "System.Collections.Generic.IEnumerable<int>"), A.ReturnsSequenceDecide);

    [Fact]
    public Task Taking_a_sequence_is_reported() => Reports($$"""
        internal sealed class C
        {
            {{Mark}}
            public int M(System.Collections.Generic.IEnumerable<int> {|#0:values|}) => 0;
        }
        """, "C.M(IEnumerable<int>)", F(A.TakesSequence, "System.Collections.Generic.IEnumerable<int>", "values"), A.TakesSequenceDecide);

    [Fact]
    public Task Spans_and_a_struct_are_clean() => Clean($$"""
        internal sealed class C
        {
            {{Mark}}
            public int M(System.ReadOnlySpan<int> input, System.Span<int> output, in System.DateTime at) => input.Length;
        }
        """);

    [Fact]
    public Task Returning_a_task_is_reported() => Reports($$"""
        internal sealed class C
        {
            {{Mark}}
            public System.Threading.Tasks.Task<int> {|#0:M|}() => System.Threading.Tasks.Task.FromResult(0);
        }
        """, "C.M()", F(A.ReturnsTask, "System.Threading.Tasks.Task<int>"), A.ReturnsTaskDecide);

    [Fact]
    public Task Taking_a_task_is_reported() => Reports($$"""
        internal sealed class C
        {
            {{Mark}}
            public int M(System.Threading.Tasks.Task<int> {|#0:pending|}) => 0;
        }
        """, "C.M(Task<int>)", F(A.TakesTask, "System.Threading.Tasks.Task<int>", "pending"), A.TakesTaskDecide);

    [Fact]
    public Task Returning_a_value_task_is_clean() => Clean($$"""
        internal sealed class C
        {
            {{Mark}}
            public System.Threading.Tasks.ValueTask<int> M() => new System.Threading.Tasks.ValueTask<int>(0);
        }
        """);

    [Fact]
    public Task Taking_an_interface_that_is_not_a_hot_contract_is_reported() => Reports($$"""
        internal interface ISource { int Next(); }

        internal sealed class C
        {
            {{Mark}}
            public int M(ISource {|#0:source|}) => 0;
        }
        """, "C.M(ISource)", F(A.TakesInterface, "ISource", "source"), A.TakesInterfaceDecide);

    [Fact]
    public Task Taking_a_contract_that_is_itself_a_hot_path_is_clean() => Clean($$"""
        [Contract(typeof(BriefConstraints.AllocationPerQuadIsADefect), Role = "source")]
        internal interface ISource
        {
            {{Mark}}
            int Next();
        }

        internal sealed class C
        {
            {{Mark}}
            public int M(ISource source) => 0;
        }
        """);

    [Fact]
    public Task Taking_a_contract_with_one_member_not_on_the_hot_path_is_reported() => Reports($$"""
        [Contract(typeof(BriefConstraints.AllocationPerQuadIsADefect), Role = "source")]
        internal interface ISource
        {
            {{Mark}}
            int Next();

            void Reset();
        }

        internal sealed class C
        {
            {{Mark}}
            public int M(ISource {|#0:source|}) => 0;
        }
        """, "C.M(ISource)", F(A.TakesInterface, "ISource", "source"), A.TakesInterfaceDecide);

    [Fact]
    public Task A_type_parameter_constrained_to_an_interface_is_clean() => Clean($$"""
        internal interface ISource { int Next(); }

        internal sealed class C
        {
            {{Mark}}
            public int M<TSource>(TSource source) where TSource : ISource => 0;
        }
        """);

    [Fact]
    public Task A_member_carrying_a_design_decision_is_not_reported() => Clean($$"""
        internal sealed class C
        {
            {{Mark}}
            [DesignDecision(typeof(BriefConstraints.AllocationPerQuadIsADefect), Scope = ExceptionScope.HotPath)]
            public System.Threading.Tasks.Task M() => System.Threading.Tasks.Task.CompletedTask;
        }
        """);
}
