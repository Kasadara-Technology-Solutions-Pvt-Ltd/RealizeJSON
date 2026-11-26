using System.Linq;
using System.Text.Json;
using RealizeJSON.Core;
using RealizeJSON.Core.Contracts;
using RealizeJSON.Diagnostics;
using RealizeJSON.Tests.Shared;

namespace RealizeJSON.Serialization.Tests;

public sealed class RuleTests
{
    private static HarnessResult Run(string type, SerializationProfile profile, PreviewOptions? options = null) =>
        PreviewHarness.Run(Fixture.Compilation, Fixture.Type(type), profile, options);

    [Fact]
    public void Acceptance_scenario_customer_dto_with_web_defaults()
    {
        var result = Run("CustomerDTO", PreviewHarness.Stj(ProfilePreset.AspNetCoreWeb, indented: true));
        Assert.Equal("{\n  \"id\": 101,\n  \"name\": \"Sample Customer\",\n  \"isActive\": true\n}", result.Json);
        Assert.False(result.Member("Password").Included);
        Assert.Contains("[JsonIgnore]", result.Member("Password").Provenance);
    }

    [Fact]
    public void Unchanged_naming_preserves_declared_names()
    {
        var result = Run("CustomerDTO", PreviewHarness.Stj());
        Assert.Equal("{\"Id\":101,\"name\":\"Sample Customer\",\"IsActive\":true}", result.Json);
        var naming = Run("NamingSamples", PreviewHarness.Stj());
        Assert.Contains("\"myProp\"", naming.Json, System.StringComparison.Ordinal);
        Assert.Contains("\"URLValue\"", naming.Json, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Excluded_members_are_reported_with_reasons()
    {
        var result = Run("StjAttributes", PreviewHarness.Stj());
        Assert.False(result.Member("WriteOnly").Included);
        Assert.False(result.Member("Secret").Included);
        Assert.False(result.Member("PlainField").Included);
        Assert.Contains(result.Member("PlainField").Explanations, e => e.Contains("IncludeFields", System.StringComparison.Ordinal));
        Assert.True(result.Member("IncludedField").Included);
        Assert.True(result.Member("InternalCounter").Included);
        Assert.True(result.Member("Code").IsRequired);
        Assert.True(result.Member("Quantity").NumberAsString);
        Assert.DoesNotContain(result.RootObject.Members, m => m.ClrName == "StaticCount" || m.ClrName == "this[]");
        var inspector = InspectorBuilder.Build(result.Contract.Root);
        Assert.Contains(inspector, r => r.ClrMember == "Secret" && !r.Included);
    }

    [Fact]
    public void Order_attributes_follow_serializer_rules()
    {
        var stj = Run("StjAttributes", PreviewHarness.Stj());
        var names = stj.RootObject.IncludedMembers.Select(m => m.JsonName).ToList();
        Assert.Equal("First", names[0]);
        Assert.Equal("Last", names[^1]);

        var ns = Run("NsAttributes", PreviewHarness.Newtonsoft());
        var nsNames = ns.RootObject.IncludedMembers.Select(m => m.JsonName).ToList();
        Assert.Equal("First", nsNames[0]);
        Assert.Equal("Second", nsNames[^1]);
    }

    [Fact]
    public void Unknown_converter_is_reported_and_not_executed()
    {
        var result = Run("UnknownConverterTarget", PreviewHarness.Stj());
        Assert.True(result.HasDiagnostic(DiagnosticIds.UnknownConverter));
        Assert.Equal(Classification.Unsupported, result.Member("Value").Classification);
    }

    [Fact]
    public void Duplicate_json_names_are_errors()
    {
        var result = Run("DuplicateNames", PreviewHarness.Stj());
        Assert.True(result.HasDiagnostic(DiagnosticIds.DuplicateJsonName));
        Assert.Equal("{\"value\":42}", result.Json);
    }

    [Fact]
    public void Unsupported_and_unresolved_types_produce_diagnostics_not_crashes()
    {
        var stj = Run("Unsupported", PreviewHarness.Stj());
        Assert.True(stj.HasDiagnostic(DiagnosticIds.UnresolvedRuntimeType));
        Assert.True(stj.HasDiagnostic(DiagnosticIds.UnsupportedType));
        Assert.True(stj.HasDiagnostic(DiagnosticIds.UnsupportedMultiDimensionalArray));
        Assert.Equal(Classification.Unresolved, stj.Member("Anything").Classification);
        Assert.Equal(Classification.Unsupported, stj.Member("Handle").Classification);
        AssertValidJson(stj.Json);

        var ns = Run("Unsupported", PreviewHarness.Newtonsoft());
        Assert.False(ns.HasDiagnostic(DiagnosticIds.UnsupportedMultiDimensionalArray));
        AssertValidJson(ns.Json);
    }

    [Fact]
    public void Newtonsoft_multidimensional_arrays_match_real_serializer()
    {
        var (expected, actual, _) = DifferentialHarnessCache.Compare("Fixtures.MultiDim", PreviewHarness.Newtonsoft(), new PreviewOptions { CollectionCount = 2 });
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Open_generic_parameters_are_unresolved()
    {
        var definition = Fixture.Type("CreateDTO`1");
        var result = PreviewHarness.Run(Fixture.Compilation, definition, PreviewHarness.Stj());
        Assert.True(result.HasDiagnostic(DiagnosticIds.OpenGenericParameter));
        Assert.Contains("\"Payload\":null", result.Json, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Constructed_generic_types_are_resolved()
    {
        var definition = Fixture.Type("CreateDTO`1");
        var constructed = definition.Construct(Fixture.Type("CaseDTO"));
        var result = PreviewHarness.Run(Fixture.Compilation, constructed, PreviewHarness.Stj(ProfilePreset.AspNetCoreWeb));
        Assert.Equal("{\"requestId\":\"requestid-001\",\"payload\":{\"caseNumber\":42,\"title\":\"Sample title\"},\"history\":[{\"caseNumber\":42,\"title\":\"Sample title\"}]}", result.Json);
        Assert.False(result.HasDiagnostic(DiagnosticIds.OpenGenericParameter));
    }

    [Fact]
    public void Polymorphic_declared_view_and_variants()
    {
        var declared = Run("Animal", PreviewHarness.Stj(ProfilePreset.AspNetCoreWeb));
        var poly = declared.RootObject.Polymorphism;
        Assert.NotNull(poly);
        Assert.Equal("kind", poly!.DiscriminatorPropertyName);
        Assert.Equal(new[] { "\"dog\"", "\"cat\"" }, poly.Variants.Select(v => v.DiscriminatorJson));
        Assert.Equal("{\"name\":\"Sample Animal\"}", declared.Json);

        var dog = Run("Animal", PreviewHarness.Stj(ProfilePreset.AspNetCoreWeb), new PreviewOptions { PolymorphicVariant = "Fixtures.Dog" });
        Assert.StartsWith("{\"kind\":\"dog\"", dog.Json, System.StringComparison.Ordinal);

        var zoo = Run("Zoo", PreviewHarness.Stj());
        Assert.True(zoo.HasDiagnostic(DiagnosticIds.PolymorphicRuntimeType));
    }

    [Fact]
    public void Cycles_are_detected_on_the_traversal_path_only()
    {
        var tree = Run("TreeNode", PreviewHarness.Stj());
        Assert.True(tree.HasDiagnostic(DiagnosticIds.PotentialCycle));
        Assert.Equal("{\"Label\":\"Sample label\",\"Parent\":null,\"Children\":[]}", tree.Json);

        var siblings = Run("Siblings", PreviewHarness.Stj());
        Assert.False(siblings.HasDiagnostic(DiagnosticIds.PotentialCycle));
        Assert.Contains("\"Work\":{", siblings.Json, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Depth_limit_produces_valid_incomplete_preview()
    {
        var result = Run("Deep1", PreviewHarness.Stj(), new PreviewOptions { MaxDepth = 2 });
        Assert.True(result.HasDiagnostic(DiagnosticIds.DepthLimit));
        Assert.True(result.Samples.Incomplete);
        AssertValidJson(result.Json);
        Assert.Equal("{\"Next\":{\"Next\":null}}", result.Json);
    }

    [Fact]
    public void Node_limit_produces_valid_incomplete_preview()
    {
        var result = Run("Wide", PreviewHarness.Stj(), new PreviewOptions { CollectionCount = 5, MaxNodes = 30 });
        Assert.True(result.HasDiagnostic(DiagnosticIds.NodeLimit));
        Assert.True(result.Samples.Incomplete);
        AssertValidJson(result.Json);
    }

    [Fact]
    public void Output_size_limit_produces_valid_truncated_json()
    {
        var result = Run("Wide", PreviewHarness.Stj(indented: true), new PreviewOptions { CollectionCount = 5, MaxBytes = 1024, MaxNodes = 100000 });
        Assert.True(result.Written.Truncated);
        Assert.True(result.Json.Length <= 1024 + 64);
        AssertValidJson(result.Json);
    }

    [Theory]
    [InlineData(SerializerKind.SystemTextJson)]
    [InlineData(SerializerKind.NewtonsoftJson)]
    public void Every_fixture_type_produces_valid_deterministic_json(SerializerKind serializer)
    {
        var profile = ProfilePresets.Create(serializer, ProfilePreset.AspNetCoreWeb);
        foreach (var type in Fixture.Compilation.Assembly.GlobalNamespace.GetNamespaceMembers().First(n => n.Name == "Fixtures").GetTypeMembers())
        {
            if (type.IsStatic || type.TypeKind == Microsoft.CodeAnalysis.TypeKind.Delegate)
            {
                continue;
            }

            foreach (var mode in new[] { SampleMode.SampleValues, SampleMode.ClrDefaults })
            {
                var options = new PreviewOptions { Mode = mode, CollectionCount = 3 };
                var first = PreviewHarness.Run(Fixture.Compilation, type, profile, options).Json;
                var second = PreviewHarness.Run(Fixture.Compilation, type, profile, options).Json;
                Assert.Equal(first, second);
                AssertValidJson(first);
            }
        }
    }

    [Fact]
    public void Extension_data_is_conditional_and_not_fabricated()
    {
        var result = Run("WithExtensionData", PreviewHarness.Stj());
        Assert.True(result.HasDiagnostic(DiagnosticIds.ExtensionDataEstimated));
        Assert.Equal("{\"Id\":101}", result.Json);
    }

    [Fact]
    public void Newtonsoft_should_serialize_is_conditional()
    {
        var result = Run("NsShouldSerialize", PreviewHarness.Newtonsoft());
        Assert.True(result.HasDiagnostic(DiagnosticIds.ConditionalMember));
        Assert.Equal(Classification.Conditional, result.Member("Name").Classification);
    }

    [Fact]
    public void Interface_members_resolved_for_stj_but_conditional_for_newtonsoft()
    {
        var stj = Run("ShapeHolder", PreviewHarness.Stj());
        Assert.Equal("{\"Shape\":{\"Area\":12.5}}", stj.Json);
        var ns = Run("ShapeHolder", PreviewHarness.Newtonsoft());
        Assert.True(ns.HasDiagnostic(DiagnosticIds.UnresolvedRuntimeType));
    }

    [Fact]
    public void Computed_members_are_estimated()
    {
        var result = Run("ComputedMembers", PreviewHarness.Stj());
        Assert.True(result.Member("Total").IsComputed);
        Assert.Equal(Classification.Estimated, result.Member("Total").Classification);
        Assert.False(result.Member("Quantity").IsComputed);
    }

    [Fact]
    public void Unsupported_dictionary_keys_are_reported()
    {
        var result = Run("BadKeys", PreviewHarness.Stj());
        Assert.True(result.HasDiagnostic(DiagnosticIds.UnsupportedDictionaryKey));
        Assert.Equal("{\"ByAddress\":{}}", result.Json);
    }

    [Fact]
    public void Json_constructor_is_metadata_only()
    {
        var result = Run("WithConstructor", PreviewHarness.Stj());
        Assert.NotNull(result.RootObject.ConstructorMetadata);
        Assert.Equal("{\"Id\":101}", result.Json);
    }

    [Fact]
    public void Flags_enum_combinations_use_member_names()
    {
        var contract = (EnumContract)Run("Order", PreviewHarness.Stj() with { Enums = EnumRepresentation.Strings }).Member("Access").Contract!;
        Assert.Equal("Read, Write", ScalarFormatting.FormatEnumName(contract, 3));
        Assert.Null(ScalarFormatting.FormatEnumName(contract, 64));
    }

    [Fact]
    public void Clr_defaults_mode_applies_omission_to_clr_values()
    {
        var profile = PreviewHarness.Stj() with { IgnoreCondition = DefaultIgnoreCondition.WhenWritingNull };
        var result = Run("Nullables", profile, new PreviewOptions { Mode = SampleMode.ClrDefaults });
        Assert.Equal("{}", result.Json);
        var sample = Run("Nullables", profile);
        Assert.Contains("\"MaybeCount\":3", sample.Json, System.StringComparison.Ordinal);
    }

    internal static void AssertValidJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.NotNull(document);
    }
}
