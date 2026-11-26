using RealizeJSON.Diagnostics;

namespace RealizeJSON.Core.Tests;

public sealed class ProfileTests
{
    [Fact]
    public void Library_defaults_do_not_assume_camel_case()
    {
        var stj = ProfilePresets.Create(SerializerKind.SystemTextJson, ProfilePreset.LibraryDefaults);
        Assert.Equal(NamingPolicyKind.Unchanged, stj.PropertyNaming);
        Assert.Equal(EnumRepresentation.Numbers, stj.Enums);
        var web = ProfilePresets.Create(SerializerKind.SystemTextJson, ProfilePreset.AspNetCoreWeb);
        Assert.Equal(NamingPolicyKind.CamelCase, web.PropertyNaming);
        Assert.Equal(NamingPolicyKind.Unchanged, web.DictionaryKeyNaming);
    }

    [Fact]
    public void Precedence_is_manual_over_detected_over_preset()
    {
        var detected = new DetectedConfiguration
        {
            Serializer = SerializerKind.SystemTextJson,
            Settings = new ProfileOverrides { PropertyNaming = NamingPolicyKind.SnakeCaseLower, Enums = EnumRepresentation.Strings },
            UnknownSettings = new[] { "TypeInfoResolver" },
        };
        var manual = new ProfileOverrides { PropertyNaming = NamingPolicyKind.KebabCaseLower };

        var profile = ProfileResolver.Resolve(SerializerKind.SystemTextJson, ProfilePreset.AspNetCoreWeb, detected, manual);

        Assert.Equal(NamingPolicyKind.KebabCaseLower, profile.PropertyNaming);
        Assert.Equal(SettingOrigin.Manual, profile.OriginOf(ProfileSettings.PropertyNaming));
        Assert.Equal(EnumRepresentation.Strings, profile.Enums);
        Assert.Equal(SettingOrigin.Detected, profile.OriginOf(ProfileSettings.Enums));
        Assert.Equal(SettingOrigin.PresetDefault, profile.OriginOf(ProfileSettings.IgnoreCondition));
        Assert.Equal(SettingOrigin.Unknown, profile.OriginOf("TypeInfoResolver"));
    }

    [Fact]
    public void Detected_settings_for_other_serializer_are_ignored()
    {
        var detected = new DetectedConfiguration { Serializer = SerializerKind.NewtonsoftJson, Settings = new ProfileOverrides { PropertyNaming = NamingPolicyKind.SnakeCaseLower } };
        var profile = ProfileResolver.Resolve(SerializerKind.SystemTextJson, ProfilePreset.LibraryDefaults, detected, null);
        Assert.Equal(NamingPolicyKind.Unchanged, profile.PropertyNaming);
    }

    [Fact]
    public void Only_meaningful_settings_are_exposed_per_serializer()
    {
        Assert.Contains(ProfileSettings.IncludeFields, ProfileSettings.For(SerializerKind.SystemTextJson));
        Assert.DoesNotContain(ProfileSettings.IncludeFields, ProfileSettings.For(SerializerKind.NewtonsoftJson));
        Assert.NotNull(ProfileSettings.ExplainUnsupported(SerializerKind.NewtonsoftJson, ProfileSettings.IncludeFields));
        Assert.Null(ProfileSettings.ExplainUnsupported(SerializerKind.SystemTextJson, ProfileSettings.IncludeFields));
    }

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(9, 5)]
    [InlineData(2, 2)]
    public void Collection_count_is_clamped(int requested, int expected)
    {
        Assert.Equal(expected, new PreviewOptions { CollectionCount = requested }.Normalize().CollectionCount);
    }

    [Fact]
    public void Default_limits_match_specification()
    {
        var options = new PreviewOptions();
        Assert.Equal(1, options.CollectionCount);
        Assert.Equal(8, options.MaxDepth);
        Assert.Equal(256 * 1024, options.MaxBytes);
        Assert.Equal(2000, options.MaxNodes);
    }

    [Fact]
    public void Diagnostic_catalog_is_consistent()
    {
        foreach (var descriptor in DiagnosticCatalog.All)
        {
            Assert.Matches("^RJ[0-9]{4}$", descriptor.Id);
            var placeholders = System.Text.RegularExpressions.Regex.Matches(descriptor.MessageFormat, @"\{(\d)\}").Count;
            var args = new object[placeholders];
            for (var i = 0; i < args.Length; i++)
            {
                args[i] = "x";
            }

            var diagnostic = DiagnosticCatalog.Create(descriptor.Id, null, null, args);
            Assert.DoesNotContain("{0}", diagnostic.Message, System.StringComparison.Ordinal);
        }
    }
}
