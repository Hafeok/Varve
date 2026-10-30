// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Varve.Server.Commands;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// ADR 0234 point 4 through <c>varve apply</c> against the real host: a
/// classifier binding with a subject-template write grant is applied, and
/// each of the five row preconditions is refused by the server's management
/// validator, naming the grant and the binding, with the declarations in
/// force before left as they were.
/// </summary>
public sealed class RowApplyTests : IDisposable
{
    private const string Vocabulary = "https://w3id.org/varve/ns#";
    private const string Graph = "https://w3id.org/varve/ns#manage/dataset/portfolio";
    private const string TenantUsers = "https://portfolio.example/manage/tenant-users";
    private const string Rows = "https://portfolio.example/manage/rows";
    private const string Refused = "https://portfolio.example/manage/refused";

    // The operator page's declaration: a classifier binding over the site
    // graphs, and a write grant over every graph to the caller's own tenant.
    private const string Valid = """
        <https://portfolio.example/manage/portfolio> a varve:Dataset ;
          varve:datasetName "portfolio" ; varve:storage varve:Memory .
        <https://portfolio.example/manage/tenants> a varve:Role ; varve:claimValue "Portfolio.Tenant" .
        <https://portfolio.example/manage/rows> a varve:ClassifierBinding ;
          varve:boundGraph <https://portfolio.example/shapes/rows> ;
          varve:graphPrefix "https://portfolio.example/graph/site/" .
        <https://portfolio.example/manage/tenant-users> a varve:Grant ;
          varve:grantee <https://portfolio.example/manage/tenants> ;
          varve:permission varve:write ; varve:allGraphs true ;
          varve:subjectTemplate <https://portfolio.example/manage/own-tenant> ;
          varve:createUnder <https://portfolio.example/manage/own-tenant-items> .
        <https://portfolio.example/manage/own-tenant> a varve:SubjectTemplate ;
          varve:template "https://portfolio.example/tenant/{claim.tenant}" ; varve:match varve:Exact .
        <https://portfolio.example/manage/own-tenant-items> a varve:SubjectTemplate ;
          varve:template "https://portfolio.example/tenant/{claim.tenant}/" ; varve:match varve:Prefix .
        """;

    private readonly string _root = Directory.CreateTempSubdirectory("varve-row-apply-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>
    /// Each precondition, as what the refused file adds to the valid
    /// declaration, the focus and path of the finding the refusal reports, and
    /// the names and rule its message carries: the grant is the focus or is
    /// named, and so is every binding it is refused over.
    /// </summary>
    public static TheoryData<string, string, string, string, string[]> Breaches => new()
    {
        {
            "P1",
            "<https://portfolio.example/manage/refused> a varve:Grant ; varve:grantee <https://portfolio.example/manage/tenants> ; "
                + "varve:permission varve:read ; varve:graph <https://portfolio.example/graph/site/1> ; varve:subject <https://portfolio.example/tenant/a> .",
            Refused,
            Vocabulary + "graph",
            ["<" + Rows + ">", "covers every graph of the classifier binding", "(ADR 0234, P1)"]
        },
        {
            "P2",
            "<https://portfolio.example/manage/refused> a varve:Grant ; varve:grantee <https://portfolio.example/manage/tenants> ; "
                + "varve:permission varve:read ; varve:graphTemplate <https://portfolio.example/manage/site-of> ; varve:subject <https://portfolio.example/tenant/a> . "
                + "<https://portfolio.example/manage/site-of> a varve:GraphTemplate ; varve:template \"https://portfolio.example/graph/site/{sub}\" ; varve:match varve:Prefix .",
            Refused,
            Vocabulary + "graphTemplate",
            ["names its graphs without varve:graphTemplate", "(ADR 0234, P2)"]
        },
        {
            "P3",
            "<https://portfolio.example/manage/refused> a varve:Grant ; varve:grantee <https://portfolio.example/manage/tenants> ; "
                + "varve:permission varve:read ; varve:graph <https://portfolio.example/graph/office> ; varve:subject <https://portfolio.example/tenant/a> .",
            Refused,
            Vocabulary + "subject",
            ["this one meets none", "(ADR 0234, P3)"]
        },
        {
            "P4",
            "<https://portfolio.example/manage/refused> a varve:ClassifierBinding ; varve:boundGraph <https://portfolio.example/shapes/other> ; "
                + "varve:graph <https://portfolio.example/graph/site/2> .",
            Rows,
            Vocabulary + "graph",
            ["<" + Rows + ">", "<" + Refused + ">", "classify a graph in common", "(ADR 0234, P4)"]
        },
        {
            "P5",
            "<https://portfolio.example/manage/refused> a varve:ShapesBinding ; varve:boundGraph <https://portfolio.example/shapes/gate> ; "
                + "varve:validationMode varve:Reject ; varve:allGraphs true .",
            Refused,
            Vocabulary + "validationMode",
            ["<" + Refused + ">", "<" + Rows + ">", "<" + TenantUsers + ">", "(ADR 0234, P5)"]
        },
    };

    [Fact]
    public async Task A_classifier_binding_and_a_subject_template_grant_are_applied_and_in_force()
    {
        await using TestIssuer issuer = await TestIssuer.StartAsync();
        await using RunningServer server = await StartServerAsync(issuer);
        string token = issuer.MintManagement(Operator());
        string valid = Write("portfolio.trig", Valid);

        Run applied = await RunAsync("apply", valid, "--server", server.Address.ToString(), "--token", token);
        Assert.True(applied.Exit == 0, applied.Error);
        Assert.Contains("Applied at system position", applied.Out, StringComparison.Ordinal);
        Assert.Equal(0, (await RunAsync("apply", valid, "--server", server.Address.ToString(), "--plan", "--exit-code", "--token", token)).Exit);

        string graphs = await ManagementGraphsAsync(server, token);
        Assert.Contains("<" + Rows + ">", graphs, StringComparison.Ordinal);
        Assert.Contains("<" + TenantUsers + ">", graphs, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Breaches))]
    public async Task Apply_refuses_a_row_precondition_naming_it_and_leaves_the_declarations_in_force(string rule, string added, string focus, string path, string[] named)
    {
        await using TestIssuer issuer = await TestIssuer.StartAsync();
        await using RunningServer server = await StartServerAsync(issuer);
        string token = issuer.MintManagement(Operator());
        string url = server.Address.ToString();
        string valid = Write("portfolio.trig", Valid);
        Assert.True((await RunAsync("apply", valid, "--server", url, "--token", token)).Exit == 0, rule);
        string before = await ManagementGraphsAsync(server, token);

        string breach = Write("breach.trig", Valid + added + "\n");
        Run refused = await RunAsync("apply", breach, "--server", url, "--token", token);

        Assert.True(refused.Exit == 1, refused.Out + refused.Error);
        Assert.DoesNotContain("Applied at system position", refused.Out, StringComparison.Ordinal);
        const string Answer = "the server refused (422, ";
        int start = refused.Error.IndexOf(Answer, StringComparison.Ordinal);
        Assert.True(start >= 0, refused.Error);
        string problem = refused.Error[(start + Answer.Length)..].TrimEnd().TrimEnd(')');
        using JsonDocument document = JsonDocument.Parse(problem);
        Assert.Equal("https://w3id.org/varve/problems/declaration-invalid", document.RootElement.GetProperty("type").GetString());
        JsonElement finding = Assert.Single(document.RootElement.GetProperty("findings").EnumerateArray());
        Assert.Equal(focus, finding.GetProperty("focus").GetString());
        Assert.Equal(Graph, finding.GetProperty("graph").GetString());
        Assert.Equal(path, finding.GetProperty("path").GetString());
        string message = finding.GetProperty("message").GetString()!;
        Assert.All(named, name => Assert.Contains(name, message, StringComparison.Ordinal));
        Assert.Equal(message, document.RootElement.GetProperty("detail").GetString());

        // The refused declaration took no effect: the server holds what the
        // valid file says, and nothing of the breach.
        string after = await ManagementGraphsAsync(server, token);
        Assert.Equal(before, after);
        Assert.DoesNotContain(Refused, after, StringComparison.Ordinal);
        Assert.Equal(0, (await RunAsync("apply", valid, "--server", url, "--plan", "--exit-code", "--token", token)).Exit);
    }

    private static async Task<string> ManagementGraphsAsync(RunningServer server, string token)
    {
        using HttpRequestMessage read = new(HttpMethod.Get, new Uri("manage/graphs", UriKind.Relative));
        read.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await server.Client.SendAsync(read, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static Dictionary<string, object> Operator() => new() { ["sub"] = "deployer", ["roles"] = new[] { "operator" } };

    private string Write(string name, string body)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, "@prefix varve: <https://w3id.org/varve/ns#> .\n<" + Graph + "> {\n" + body + "}\n", Encoding.UTF8);
        return path;
    }

    private static Task<RunningServer> StartServerAsync(TestIssuer issuer) => RunningServer.StartAsync(new Dictionary<string, string>
    {
        ["Varve:Auth:Mode"] = "Oidc",
        ["Varve:Auth:Authority"] = issuer.Issuer,
        ["Varve:Auth:Audiences:0"] = TestIssuer.Audience,
        ["Varve:Auth:RequireHttpsMetadata"] = "false",
        ["Varve:Manage:BootstrapOperators:0"] = "operator",
    });

    private sealed record Run(int Exit, string Out, string Error);

    private static async Task<Run> RunAsync(params string[] args)
    {
        StringWriter output = new() { NewLine = "\n" };
        StringWriter error = new() { NewLine = "\n" };
        int exit = await Cli.RunAsync(args, new Io(output, error, new MemoryStream()), TestContext.Current.CancellationToken);
        return new Run(exit, output.ToString(), error.ToString());
    }
}
