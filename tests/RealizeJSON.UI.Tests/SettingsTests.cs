using RealizeJSON.Core;

namespace RealizeJSON.UI.Tests;

public sealed class SettingsTests
{
    [Fact]
    public void Round_trip_preserves_all_values()
    {
        var settings = new UserSettings
        {
            Serializer = SerializerKind.NewtonsoftJson,
            Preset = ProfilePreset.Custom,
            SampleMode = SampleMode.ClrDefaults,
            CollectionCount = 3,
            DebounceMilliseconds = 500,
            WordWrap = true,
            Overrides = new ProfileOverrides { PropertyNaming = NamingPolicyKind.KebabCaseLower, IncludeFields = true, ReferenceHandling = ReferenceHandlingMode.Preserve },
        };

        var read = SettingsSerializer.FromJson(SettingsSerializer.ToJson(settings));
        Assert.Null(read.Warning);
        Assert.Equal(settings, read.Settings);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1,2]")]
    public void Malformed_settings_fall_back_to_defaults_with_warning(string json)
    {
        var read = SettingsSerializer.FromJson(json);
        Assert.NotNull(read.Warning);
        Assert.Equal(new UserSettings(), read.Settings);
    }

    [Fact]
    public void Invalid_values_are_ignored_or_clamped()
    {
        var read = SettingsSerializer.FromJson("{\"schemaVersion\":2,\"serializer\":\"Bogus\",\"collectionCount\":99,\"debounceMs\":-5}");
        Assert.Equal(SerializerKind.SystemTextJson, read.Settings.Serializer);
        Assert.Equal(5, read.Settings.CollectionCount);
        Assert.Equal(0, read.Settings.DebounceMilliseconds);
    }

    [Fact]
    public void Version_1_settings_are_migrated()
    {
        var read = SettingsSerializer.FromJson("{\"debounce\":200,\"webDefaults\":false}");
        Assert.True(read.WasMigrated);
        Assert.Equal(200, read.Settings.DebounceMilliseconds);
        Assert.Equal(ProfilePreset.LibraryDefaults, read.Settings.Preset);
    }

    [Fact]
    public void Newer_schema_versions_are_not_misread()
    {
        var read = SettingsSerializer.FromJson("{\"schemaVersion\":99,\"collectionCount\":3}");
        Assert.NotNull(read.Warning);
        Assert.Equal(1, read.Settings.CollectionCount);
    }

    [Fact]
    public void Solution_profile_overlays_user_settings()
    {
        var json = SolutionProfile.ToJson(SerializerKind.NewtonsoftJson, ProfilePreset.LibraryDefaults, new ProfileOverrides { PropertyNaming = NamingPolicyKind.SnakeCaseLower });
        var (applied, warning) = SolutionProfile.Apply(new UserSettings(), json);
        Assert.Null(warning);
        Assert.Equal(SerializerKind.NewtonsoftJson, applied.Serializer);
        Assert.Equal(NamingPolicyKind.SnakeCaseLower, applied.Overrides.PropertyNaming);

        var (unchanged, malformed) = SolutionProfile.Apply(new UserSettings(), "{oops");
        Assert.NotNull(malformed);
        Assert.Equal(new UserSettings(), unchanged);
    }
}
