using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RealizeJSON.Core;
using RealizeJSON.Diagnostics;
using RealizeJSON.Serialization;
using RealizeJSON.Tests.Shared;

namespace RealizeJSON.Integration.Tests;

public sealed class EngineIntegrationTests
{
    private const string Customer = """
        using System.Text.Json.Serialization;

        public class CustomerDTO
        {
            public int Id { get; set; }

            [JsonPropertyName("name")]
            public string FullName { get; set; } = string.Empty;

            public bool IsActive { get; set; }

            [JsonIgnore]
            public string Password { get; set; } = string.Empty;
        }
        """;

    private static readonly PreviewEngine Engine = WorkspaceFactory.CreateEngine();

    private static PreviewRequest Request(Document document, int caret, ProfilePreset preset = ProfilePreset.AspNetCoreWeb) =>
        new(document, caret) { Preset = preset, Options = new PreviewOptions() };

    [Fact]
    public async Task Acceptance_scenario_including_unsaved_edit_without_build()
    {
        var (workspace, project) = WorkspaceFactory.CreateProject("Api", ("CustomerDTO.cs", Customer));
        using var _ = workspace;
        var document = project.Documents.Single();

        var first = await Engine.GenerateAsync(Request(document, WorkspaceFactory.Caret(document, "CustomerDTO", 2)), CancellationToken.None);
        Assert.Equal(PreviewState.Live, first.State);
        Assert.Equal("{\n  \"id\": 101,\n  \"name\": \"Sample Customer\",\n  \"isActive\": true\n}", first.Json);
        Assert.Equal("Static preview · Sample values · System.Text.Json", first.Label);
        Assert.Equal("CustomerDTO", first.SelectedType!.FullName);
        Assert.Equal(3, first.PropertyCount);

        // Simulate an unsaved editor change: the text snapshot changes, nothing is built or emitted.
        var edited = document.WithText(SourceText.From(Customer.Replace("public bool IsActive", "public string Email { get; set; } = \"\";\n    public bool IsActive")));
        var second = await Engine.GenerateAsync(Request(edited, WorkspaceFactory.Caret(edited, "CustomerDTO", 2)), CancellationToken.None);
        Assert.Contains("\"email\": \"user@example.com\"", second.Json, StringComparison.Ordinal);
        Assert.Contains(second.Inspector, r => r.ClrMember == "Password" && !r.Included);
    }

    [Fact]
    public async Task Cross_project_types_are_resolved_from_source()
    {
        var workspace = new AdhocWorkspace();
        var contracts = WorkspaceFactory.AddProject(workspace, "Contracts", new[] { ("Address.cs", "namespace Contracts { public class Address { public string City { get; set; } = \"\"; } }") });
        var api = WorkspaceFactory.AddProject(workspace, "Api", new[] { ("Order.cs", "namespace Api { public class Order { public Contracts.Address Ship { get; set; } = new(); } }") }, new ProjectReference(contracts.Id));
        var document = workspace.CurrentSolution.GetProject(api.Id)!.Documents.Single();

        var result = await Engine.GenerateAsync(Request(document, WorkspaceFactory.Caret(document, "Order", 1)), CancellationToken.None);

        Assert.Contains("\"ship\": {\n    \"city\": \"Sample city\"\n  }", result.Json, StringComparison.Ordinal);
        Assert.Contains(result.ContributingFiles, f => f.EndsWith("Address.cs", StringComparison.Ordinal) || f == "Address.cs");
        workspace.Dispose();
    }

    [Fact]
    public async Task Metadata_only_types_are_resolved_and_marked()
    {
        var library = TestCompilation.Create("namespace Lib { public class Money { public decimal Amount { get; set; } public string Currency { get; set; } = \"\"; } }", "Lib");
        var reference = MetadataReference.CreateFromImage(TestCompilation.Emit(library));
        var (workspace, project) = WorkspaceFactory.CreateProject("Api", ("Invoice.cs", "public class Invoice { public Lib.Money Total { get; set; } = new(); }"));
        using var _ = workspace;
        var document = project.AddMetadataReference(reference).Documents.Single();

        var result = await Engine.GenerateAsync(Request(document, WorkspaceFactory.Caret(document, "Invoice", 1)), CancellationToken.None);

        Assert.Contains("\"currency\": \"USD\"", result.Json, StringComparison.Ordinal);
        Assert.Contains(result.Inspector, r => r.ClrMember == "Amount" && r.Location.Origin == SourceOrigin.Metadata);
    }

    [Fact]
    public async Task Detected_project_configuration_is_applied_with_precedence()
    {
        var program = """
            public static class Program
            {
                public static void Configure(dynamic services)
                {
                    services.AddControllers().AddJsonOptions((System.Action<dynamic>)(o =>
                    {
                        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
                    }));
                }
            }
            """;
        var (workspace, project) = WorkspaceFactory.CreateProject("Api", ("CustomerDTO.cs", Customer), ("Program.cs", program));
        using var _ = workspace;
        var document = project.Documents.First(d => d.Name == "CustomerDTO.cs");
        var caret = WorkspaceFactory.Caret(document, "CustomerDTO", 2);

        var detected = await Engine.GenerateAsync(Request(document, caret), CancellationToken.None);
        Assert.Contains("\"is_active\"", detected.Json, StringComparison.Ordinal);
        Assert.Equal(SettingOrigin.Detected, detected.Profile!.OriginOf(ProfileSettings.PropertyNaming));
        Assert.StartsWith("Detected: Program.cs", detected.ConfigurationSummary, StringComparison.Ordinal);

        var manual = await Engine.GenerateAsync(Request(document, caret) with { Overrides = new ProfileOverrides { PropertyNaming = NamingPolicyKind.KebabCaseLower } }, CancellationToken.None);
        Assert.Contains("\"is-active\"", manual.Json, StringComparison.Ordinal);

        var off = await Engine.GenerateAsync(Request(document, caret) with { ConfigurationMode = ConfigurationMode.Off }, CancellationToken.None);
        Assert.Contains("\"isActive\"", off.Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Multiple_configurations_are_reported_as_ambiguous_and_selectable()
    {
        var program = """
            class P { void M(dynamic s) {
                s.AddJsonOptions((System.Action<dynamic>)(o => { o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower; }));
                s.ConfigureHttpJsonOptions((System.Action<dynamic>)(o => { o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower; }));
            } }
            """;
        var (workspace, project) = WorkspaceFactory.CreateProject("Api", ("CustomerDTO.cs", Customer), ("Program.cs", program));
        using var _ = workspace;
        var document = project.Documents.First(d => d.Name == "CustomerDTO.cs");
        var caret = WorkspaceFactory.Caret(document, "CustomerDTO", 2);

        var first = await Engine.GenerateAsync(Request(document, caret), CancellationToken.None);
        Assert.Contains(first.Diagnostics, d => d.Id == DiagnosticIds.ConfigurationAmbiguous);
        Assert.Contains("\"is_active\"", first.Json, StringComparison.Ordinal);
        var second = await Engine.GenerateAsync(Request(document, caret) with { SelectedConfiguration = 1 }, CancellationToken.None);
        Assert.Contains("\"is-active\"", second.Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pinned_type_is_stable_when_caret_moves_to_another_type()
    {
        var (workspace, project) = WorkspaceFactory.CreateProject("Api", ("Types.cs", Customer + "\npublic class Other { public int X { get; set; } }"));
        using var _ = workspace;
        var document = project.Documents.Single();
        var first = await Engine.GenerateAsync(Request(document, WorkspaceFactory.Caret(document, "CustomerDTO", 2)), CancellationToken.None);

        var pinned = await Engine.GenerateAsync(Request(document, WorkspaceFactory.Caret(document, "Other", 2)) with { PinnedTypeKeyText = first.SelectedTypeKey }, CancellationToken.None);
        Assert.Equal("CustomerDTO", pinned.SelectedType!.FullName);

        var removed = document.WithText(SourceText.From("public class Other { }"));
        var missing = await Engine.GenerateAsync(Request(removed, 5) with { PinnedTypeKeyText = first.SelectedTypeKey }, CancellationToken.None);
        Assert.Contains(missing.Diagnostics, d => d.Id == DiagnosticIds.UnresolvedSymbol);
    }

    [Fact]
    public async Task Newtonsoft_profile_change_is_applied()
    {
        var (workspace, project) = WorkspaceFactory.CreateProject("Api", ("CustomerDTO.cs", Customer));
        using var _ = workspace;
        var document = project.Documents.Single();
        var result = await Engine.GenerateAsync(Request(document, WorkspaceFactory.Caret(document, "CustomerDTO", 2)) with { Serializer = SerializerKind.NewtonsoftJson }, CancellationToken.None);

        // Newtonsoft ignores System.Text.Json attributes: Password is serialized and FullName is camel-cased.
        Assert.Contains("\"fullName\"", result.Json, StringComparison.Ordinal);
        Assert.Contains("\"password\"", result.Json, StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, d => d.Id == DiagnosticIds.SensitiveMemberHeuristic);
        Assert.Equal("Static preview · Sample values · Newtonsoft.Json", result.Label);
    }

    [Fact]
    public async Task Invalid_intermediate_syntax_is_flagged_not_thrown()
    {
        var (workspace, project) = WorkspaceFactory.CreateProject("Api", ("CustomerDTO.cs", Customer.Replace("public bool IsActive { get; set; }", "public bool IsActive { get; set")));
        using var _ = workspace;
        var document = project.Documents.Single();
        var result = await Engine.GenerateAsync(Request(document, WorkspaceFactory.Caret(document, "CustomerDTO", 2)), CancellationToken.None);
        Assert.True(result.SourceHasErrors);
        Assert.Contains(result.Diagnostics, d => d.Id == DiagnosticIds.SourceHasErrors);
    }

    [Fact]
    public async Task No_type_at_caret_is_an_empty_state()
    {
        var (workspace, project) = WorkspaceFactory.CreateProject("Api", ("CustomerDTO.cs", Customer));
        using var _ = workspace;
        var document = project.Documents.Single();
        var result = await Engine.GenerateAsync(Request(document, 3), CancellationToken.None);
        Assert.Equal(PreviewState.Empty, result.State);
        Assert.Equal(string.Empty, result.Json);
        Assert.Contains(result.Diagnostics, d => d.Id == DiagnosticIds.NoTypeAtCaret);
    }

    [Fact]
    public async Task Cancellation_is_honored()
    {
        var (workspace, project) = WorkspaceFactory.CreateProject("Api", ("CustomerDTO.cs", Customer));
        using var _ = workspace;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Engine.GenerateAsync(Request(project.Documents.Single(), 50), cts.Token));
    }

    [Fact]
    public async Task Performance_100_member_graph_p95_is_below_target()
    {
        // Engineering target (NFR-04): p95 < 500 ms after debounce for a warmed 100-member DTO graph.
        var sb = new StringBuilder("public class Big {\n");
        for (var i = 0; i < 50; i++)
        {
            sb.Append("    public string Field").Append(i).Append(" { get; set; } = \"\";\n");
        }

        for (var i = 0; i < 10; i++)
        {
            sb.Append("    public Child").Append(i).Append(" Child").Append(i).Append(" { get; set; } = new();\n");
        }

        sb.Append("}\n");
        for (var i = 0; i < 10; i++)
        {
            sb.Append("public class Child").Append(i).Append(" { public int A { get; set; } public string B { get; set; } = \"\"; public System.DateTime C { get; set; } public decimal D { get; set; } }\n");
        }

        var (workspace, project) = WorkspaceFactory.CreateProject("Perf", ("Big.cs", sb.ToString()));
        using var _ = workspace;
        var document = project.Documents.Single();
        var caret = WorkspaceFactory.Caret(document, "Big", 1);
        await Engine.GenerateAsync(Request(document, caret), CancellationToken.None);

        var timings = new double[50];
        for (var i = 0; i < timings.Length; i++)
        {
            // Each iteration uses a new text snapshot, as a keystroke would.
            var edited = document.WithText(SourceText.From(sb.ToString() + "// edit " + i));
            var stopwatch = Stopwatch.StartNew();
            var result = await Engine.GenerateAsync(Request(edited, caret), CancellationToken.None);
            timings[i] = stopwatch.Elapsed.TotalMilliseconds;
            Assert.Equal(100, result.Inspector.Count(r => r.Included));
        }

        Array.Sort(timings);
        var p95 = timings[(int)Math.Ceiling(timings.Length * 0.95) - 1];
        TestContext.Current.SendDiagnosticMessage($"NFR-04 p50={timings[timings.Length / 2]:F1}ms p95={p95:F1}ms max={timings[^1]:F1}ms ({Environment.ProcessorCount} cores, {Environment.OSVersion}, {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription})");
        System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "perf-nfr04.txt"),
            $"NFR-04 p50={timings[timings.Length / 2]:F1}ms p95={p95:F1}ms max={timings[^1]:F1}ms; cores={Environment.ProcessorCount}; os={Environment.OSVersion}; runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; roslyn={typeof(Compilation).Assembly.GetName().Version}");
        Assert.True(p95 < 500, $"p95 {p95:F1} ms exceeds the 500 ms target");
    }
}

public sealed class TargetFrameworkTests
{
    [Theory]
    [InlineData("net8.0", "NET", "NET8_0", "NET5_0_OR_GREATER", "NET6_0", "NETCOREAPP")]
    [InlineData("netstandard2.0", "NETSTANDARD", "NETSTANDARD2_0")]
    [InlineData("net472", "NETFRAMEWORK", "NET472", "NET47")]
    [InlineData(null, "DEBUG", "TRACE")]
    public void Target_framework_is_inferred_from_preprocessor_symbols(string? expected, params string[] symbols) =>
        Assert.Equal(expected, PreviewEngine.InferTargetFramework(symbols));
}
