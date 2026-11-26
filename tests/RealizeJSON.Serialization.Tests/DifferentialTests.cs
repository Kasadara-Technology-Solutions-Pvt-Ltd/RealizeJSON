using System.Collections.Generic;
using System.Linq;
using RealizeJSON.Core;
using RealizeJSON.Tests.Shared;

namespace RealizeJSON.Serialization.Tests;

/// <summary>
/// Differential tests: the static preview must equal the real serializer's output for the same modeled values
/// (values are materialized via reflection without running constructors, initializers or getters).
/// </summary>
public sealed class DifferentialTests
{
    private static readonly string[] CommonTypes =
    {
        "CustomerDTO", "Primitives", "Nullables", "Order", "OrderLine", "Dictionaries", "DerivedEntity", "PersonRecord",
        "PointRecord", "WithStruct", "GenericHolder", "StjAttributes", "WithFields", "Siblings", "TreeNode", "Tuples",
        "NsAttributes", "NsOptIn", "NsDataContract", "EnumMembers", "NamingSamples", "Address", "Deep1", "HalfValues",
    };

    public static IEnumerable<TheoryDataRow<string, SerializerKind, ProfilePreset, SampleMode>> Matrix()
    {
        foreach (var type in CommonTypes)
        {
            foreach (var serializer in new[] { SerializerKind.SystemTextJson, SerializerKind.NewtonsoftJson })
            {
                foreach (var preset in new[] { ProfilePreset.LibraryDefaults, ProfilePreset.AspNetCoreWeb })
                {
                    foreach (var mode in new[] { SampleMode.SampleValues, SampleMode.ClrDefaults })
                    {
                        yield return new(type, serializer, preset, mode);
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Preview_matches_real_serializer(string type, SerializerKind serializer, ProfilePreset preset, SampleMode mode)
    {
        var profile = ProfilePresets.Create(serializer, preset) with { WriteIndented = false };
        AssertMatches(type, profile, new PreviewOptions { Mode = mode, CollectionCount = 2 });
    }

    public static IEnumerable<TheoryDataRow<string, SerializerKind, string>> SettingsMatrix()
    {
        string[] types = { "Primitives", "Nullables", "Order", "Dictionaries", "StjAttributes", "NsAttributes", "WithFields", "NamingSamples", "EnumMembers", "TreeNode", "WithStruct" };
        string[] settings = { "ignore-null", "ignore-default", "enum-strings", "enum-strings-camel", "snake", "kebab", "snake-upper", "keys-camel", "include-fields", "ignore-readonly", "numbers-as-strings", "preserve", "indented" };
        foreach (var type in types)
        {
            foreach (var serializer in new[] { SerializerKind.SystemTextJson, SerializerKind.NewtonsoftJson })
            {
                foreach (var setting in settings)
                {
                    if (serializer == SerializerKind.NewtonsoftJson && setting is "include-fields" or "ignore-readonly" or "numbers-as-strings" or "snake-upper")
                    {
                        continue;
                    }

                    yield return new(type, serializer, setting);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(SettingsMatrix))]
    public void Preview_matches_real_serializer_with_settings(string type, SerializerKind serializer, string setting)
    {
        var profile = ProfilePresets.Create(serializer, ProfilePreset.LibraryDefaults) with { WriteIndented = false };
        profile = setting switch
        {
            "ignore-null" => profile with { IgnoreCondition = DefaultIgnoreCondition.WhenWritingNull },
            "ignore-default" => profile with { IgnoreCondition = DefaultIgnoreCondition.WhenWritingDefault },
            "enum-strings" => profile with { Enums = EnumRepresentation.Strings },
            "enum-strings-camel" => profile with { Enums = EnumRepresentation.Strings, EnumNaming = NamingPolicyKind.CamelCase },
            "snake" => profile with { PropertyNaming = NamingPolicyKind.SnakeCaseLower },
            "kebab" => profile with { PropertyNaming = NamingPolicyKind.KebabCaseLower },
            "snake-upper" => profile with { PropertyNaming = NamingPolicyKind.SnakeCaseUpper },
            "keys-camel" => serializer == SerializerKind.SystemTextJson
                ? profile with { DictionaryKeyNaming = NamingPolicyKind.CamelCase }
                : profile with { PropertyNaming = NamingPolicyKind.CamelCase, DictionaryKeyNaming = NamingPolicyKind.CamelCase },
            "include-fields" => profile with { IncludeFields = true },
            "ignore-readonly" => profile with { IgnoreReadOnlyProperties = true, IgnoreReadOnlyFields = true, IncludeFields = true },
            "numbers-as-strings" => profile with { WriteNumbersAsStrings = true },
            "preserve" => profile with { ReferenceHandling = ReferenceHandlingMode.Preserve },
            "indented" => profile with { WriteIndented = true },
            _ => profile,
        };

        foreach (var mode in new[] { SampleMode.SampleValues, SampleMode.ClrDefaults })
        {
            AssertMatches(type, profile, new PreviewOptions { Mode = mode, CollectionCount = 2 });
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Collection_counts_match_real_serializer(int count)
    {
        foreach (var serializer in new[] { SerializerKind.SystemTextJson, SerializerKind.NewtonsoftJson })
        {
            AssertMatches("Order", ProfilePresets.Create(serializer, ProfilePreset.AspNetCoreWeb) with { WriteIndented = false }, new PreviewOptions { CollectionCount = count });
            AssertMatches("Dictionaries", ProfilePresets.Create(serializer, ProfilePreset.LibraryDefaults) with { WriteIndented = false }, new PreviewOptions { CollectionCount = count });
        }
    }

    [Theory]
    [InlineData("Fixtures.Dog")]
    [InlineData("Fixtures.Cat")]
    public void Polymorphic_variant_matches_System_Text_Json(string variant)
    {
        var profile = ProfilePresets.Create(SerializerKind.SystemTextJson, ProfilePreset.AspNetCoreWeb) with { WriteIndented = false };
        AssertMatches("Animal", profile, new PreviewOptions { PolymorphicVariant = variant });
    }

    [Fact]
    public void Nullable_members_as_null_matches_real_serializer()
    {
        foreach (var serializer in new[] { SerializerKind.SystemTextJson, SerializerKind.NewtonsoftJson })
        {
            AssertMatches("Nullables", ProfilePresets.Create(serializer, ProfilePreset.LibraryDefaults) with { WriteIndented = false }, new PreviewOptions { NullableMembersAsNull = true });
        }
    }

    private static void AssertMatches(string type, SerializationProfile profile, PreviewOptions options)
    {
        var (expected, actual, preview) = DifferentialHarnessCache.Compare("Fixtures." + type, profile, options);
        Assert.True(expected == actual, $"Mismatch for {type} ({profile.Serializer}, {profile.Preset}, {options.Mode}):\nexpected: {expected}\nactual:   {actual}\ndiagnostics: {string.Join(" | ", preview.AllDiagnostics.Select(d => d.Id + " " + d.Message))}");
    }
}

/// <summary>Reuses one emitted/loaded fixture assembly across differential tests.</summary>
internal static class DifferentialHarnessCache
{
    private static readonly System.Lazy<DifferentialHarness> Harness = new(() => new DifferentialHarness(Fixture.Compilation));

    public static (string Expected, string Actual, HarnessResult Preview) Compare(string typeName, SerializationProfile profile, PreviewOptions options)
    {
        var harness = Harness.Value;
        var preview = PreviewHarness.Run(Fixture.Compilation, TestCompilation.GetType(Fixture.Compilation, typeName), profile, options);
        var runtimeType = harness.Assembly.GetType(typeName, throwOnError: true)!;
        var instance = harness.Materialize(preview.Samples.Root, runtimeType);
        var expected = DifferentialHarness.Serialize(instance, runtimeType, profile);
        return (expected, preview.Json, preview);
    }
}
