using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using RealizeJSON.Core;
using RealizeJSON.Serialization;
using RealizeJSON.UI;

namespace RealizeJSON.VisualStudio;

/// <summary>Tools &gt; Options &gt; RealizeJSON &gt; General. Persists per-user defaults in the VS settings store.</summary>
[Guid("c4d5e6f7-0819-4a2b-8c3d-4e5f60718293")]
[ComVisible(true)]
public sealed class OptionsPage : DialogPage
{
    /// <summary>Raised on the UI thread after settings are saved.</summary>
    internal static event EventHandler<UserSettings>? SettingsSaved;

    [Category("Serializer")]
    [DisplayName("Serializer")]
    [Description("Serializer whose rules are used for the preview.")]
    public SerializerKind Serializer { get; set; } = SerializerKind.SystemTextJson;

    [Category("Serializer")]
    [DisplayName("Profile preset")]
    [Description("Library defaults match the serializer's standalone defaults; ASP.NET Core web defaults match the web framework's configuration.")]
    public ProfilePreset Preset { get; set; } = ProfilePreset.AspNetCoreWeb;

    [Category("Serializer")]
    [DisplayName("Detect project configuration")]
    [Description("Statically detect AddJsonOptions/AddNewtonsoftJson configuration. Code is never executed.")]
    public bool DetectConfiguration { get; set; } = true;

    [Category("Profile overrides")]
    [DisplayName("Property naming")]
    [Description("Overrides the preset/detected naming policy. Newtonsoft.Json has no upper-case strategies.")]
    public NamingOption PropertyNaming { get; set; }

    [Category("Profile overrides")]
    [DisplayName("Dictionary key naming")]
    [Description("System.Text.Json DictionaryKeyPolicy (string and enum keys). Newtonsoft.Json: ProcessDictionaryKeys with the naming strategy.")]
    public NamingOption DictionaryKeyNaming { get; set; }

    [Category("Profile overrides")]
    [DisplayName("Enums")]
    public EnumOption Enums { get; set; }

    [Category("Profile overrides")]
    [DisplayName("Enum string naming")]
    [Description("Naming policy passed to JsonStringEnumConverter / StringEnumConverter when enums are strings.")]
    public NamingOption EnumNaming { get; set; }

    [Category("Profile overrides")]
    [DisplayName("Ignore condition")]
    [Description("System.Text.Json DefaultIgnoreCondition; Newtonsoft.Json NullValueHandling/DefaultValueHandling = Ignore.")]
    public IgnoreOption IgnoreCondition { get; set; }

    [Category("Profile overrides")]
    [DisplayName("Include fields (System.Text.Json only)")]
    [Description("Newtonsoft.Json always serializes public fields.")]
    public ToggleOption IncludeFields { get; set; }

    [Category("Profile overrides")]
    [DisplayName("Ignore read-only properties (System.Text.Json only)")]
    public ToggleOption IgnoreReadOnlyProperties { get; set; }

    [Category("Profile overrides")]
    [DisplayName("Ignore read-only fields (System.Text.Json only)")]
    public ToggleOption IgnoreReadOnlyFields { get; set; }

    [Category("Profile overrides")]
    [DisplayName("Write numbers as strings (System.Text.Json only)")]
    public ToggleOption WriteNumbersAsStrings { get; set; }

    [Category("Profile overrides")]
    [DisplayName("Reference handling")]
    [Description("Preserve: $id/$ref metadata. IgnoreCycles: STJ writes null / Newtonsoft skips looping members.")]
    public ReferenceOption ReferenceHandling { get; set; }

    [Category("Profile overrides")]
    [DisplayName("Override specified names (Newtonsoft.Json only)")]
    [Description("NamingStrategy.OverrideSpecifiedNames. System.Text.Json never renames [JsonPropertyName] names.")]
    public ToggleOption OverrideSpecifiedNames { get; set; }

    [Category("Samples")]
    [DisplayName("Values")]
    [Description("Sample values are illustrative; CLR defaults are synthetic default values (constructors and initializers are not simulated).")]
    public SampleMode SampleMode { get; set; } = SampleMode.SampleValues;

    [Category("Samples")]
    [DisplayName("Collection sample count (0-5)")]
    public int CollectionCount { get; set; } = 1;

    [Category("Samples")]
    [DisplayName("Show nullable members as null")]
    public bool NullableMembersAsNull { get; set; }

    [Category("Samples")]
    [DisplayName("Use member-name heuristics")]
    public bool UseNameHeuristics { get; set; } = true;

    [Category("Diagnostics")]
    [DisplayName("Warn about possibly sensitive member names (heuristic)")]
    public bool SensitiveNameWarnings { get; set; } = true;

    [Category("Performance")]
    [DisplayName("Refresh delay (ms)")]
    [Description("Debounce delay before analysis after edits or caret movement.")]
    public int DebounceMilliseconds { get; set; } = 350;

    [Category("Limits")]
    [DisplayName("Maximum depth")]
    public int MaxDepth { get; set; } = 8;

    [Category("Limits")]
    [DisplayName("Maximum nodes")]
    public int MaxNodes { get; set; } = 2000;

    [Category("Limits")]
    [DisplayName("Maximum output size (bytes)")]
    public int MaxBytes { get; set; } = 256 * 1024;

    public override void LoadSettingsFromStorage()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var settings = new VsSettingsStore(this.Site ?? (IServiceProvider)ServiceProvider.GlobalProvider).Load();
        Serializer = settings.Serializer;
        Preset = settings.Preset;
        DetectConfiguration = settings.ConfigurationMode == ConfigurationMode.Detect;
        SampleMode = settings.SampleMode;
        CollectionCount = settings.CollectionCount;
        NullableMembersAsNull = settings.NullableMembersAsNull;
        UseNameHeuristics = settings.UseNameHeuristics;
        SensitiveNameWarnings = settings.SensitiveNameWarnings;
        DebounceMilliseconds = settings.DebounceMilliseconds;
        MaxDepth = settings.MaxDepth;
        MaxNodes = settings.MaxNodes;
        MaxBytes = settings.MaxBytes;
        var o = settings.Overrides;
        PropertyNaming = ToOption(o.PropertyNaming);
        DictionaryKeyNaming = ToOption(o.DictionaryKeyNaming);
        EnumNaming = ToOption(o.EnumNaming);
        Enums = o.Enums switch { EnumRepresentation.Numbers => EnumOption.Numbers, EnumRepresentation.Strings => EnumOption.Strings, _ => EnumOption.ProfileDefault };
        IgnoreCondition = o.IgnoreCondition switch
        {
            DefaultIgnoreCondition.Never => IgnoreOption.Never,
            DefaultIgnoreCondition.WhenWritingNull => IgnoreOption.WhenWritingNull,
            DefaultIgnoreCondition.WhenWritingDefault => IgnoreOption.WhenWritingDefault,
            _ => IgnoreOption.ProfileDefault,
        };
        IncludeFields = ToOption(o.IncludeFields);
        IgnoreReadOnlyProperties = ToOption(o.IgnoreReadOnlyProperties);
        IgnoreReadOnlyFields = ToOption(o.IgnoreReadOnlyFields);
        WriteNumbersAsStrings = ToOption(o.WriteNumbersAsStrings);
        OverrideSpecifiedNames = ToOption(o.OverrideSpecifiedNames);
        ReferenceHandling = o.ReferenceHandling switch
        {
            ReferenceHandlingMode.None => ReferenceOption.None,
            ReferenceHandlingMode.Preserve => ReferenceOption.Preserve,
            ReferenceHandlingMode.IgnoreCycles => ReferenceOption.IgnoreCycles,
            _ => ReferenceOption.ProfileDefault,
        };
    }

    public override void SaveSettingsToStorage()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var store = new VsSettingsStore(this.Site ?? (IServiceProvider)ServiceProvider.GlobalProvider);
        var settings = (store.Load() with
        {
            Serializer = Serializer,
            Preset = Preset,
            ConfigurationMode = DetectConfiguration ? ConfigurationMode.Detect : ConfigurationMode.Off,
            SampleMode = SampleMode,
            CollectionCount = CollectionCount,
            NullableMembersAsNull = NullableMembersAsNull,
            UseNameHeuristics = UseNameHeuristics,
            SensitiveNameWarnings = SensitiveNameWarnings,
            DebounceMilliseconds = DebounceMilliseconds,
            MaxDepth = MaxDepth,
            MaxNodes = MaxNodes,
            MaxBytes = MaxBytes,
        }).Normalize();
        settings = settings with
        {
            Overrides = settings.Overrides with
            {
                PropertyNaming = FromOption(PropertyNaming),
                DictionaryKeyNaming = FromOption(DictionaryKeyNaming),
                EnumNaming = FromOption(EnumNaming),
                Enums = Enums switch { EnumOption.Numbers => EnumRepresentation.Numbers, EnumOption.Strings => EnumRepresentation.Strings, _ => null },
                IgnoreCondition = IgnoreCondition switch
                {
                    IgnoreOption.Never => DefaultIgnoreCondition.Never,
                    IgnoreOption.WhenWritingNull => DefaultIgnoreCondition.WhenWritingNull,
                    IgnoreOption.WhenWritingDefault => DefaultIgnoreCondition.WhenWritingDefault,
                    _ => null,
                },
                IncludeFields = FromOption(IncludeFields),
                IgnoreReadOnlyProperties = FromOption(IgnoreReadOnlyProperties),
                IgnoreReadOnlyFields = FromOption(IgnoreReadOnlyFields),
                WriteNumbersAsStrings = FromOption(WriteNumbersAsStrings),
                OverrideSpecifiedNames = FromOption(OverrideSpecifiedNames),
                ReferenceHandling = ReferenceHandling switch
                {
                    ReferenceOption.None => ReferenceHandlingMode.None,
                    ReferenceOption.Preserve => ReferenceHandlingMode.Preserve,
                    ReferenceOption.IgnoreCycles => ReferenceHandlingMode.IgnoreCycles,
                    _ => null,
                },
            },
        };
        store.Write(SettingsSerializer.StoreKey, SettingsSerializer.ToJson(settings));
        SettingsSaved?.Invoke(this, settings);
    }

    private static NamingOption ToOption(NamingPolicyKind? value) => value is null ? NamingOption.ProfileDefault : (NamingOption)((int)value.Value + 1);

    private static NamingPolicyKind? FromOption(NamingOption value) => value == NamingOption.ProfileDefault ? null : (NamingPolicyKind)((int)value - 1);

    private static ToggleOption ToOption(bool? value) => value switch { true => ToggleOption.Enabled, false => ToggleOption.Disabled, _ => ToggleOption.ProfileDefault };

    private static bool? FromOption(ToggleOption value) => value switch { ToggleOption.Enabled => true, ToggleOption.Disabled => false, _ => null };
}

/// <summary>Three-state option: keep the profile value or override it.</summary>
public enum ToggleOption
{
    ProfileDefault,
    Enabled,
    Disabled,
}

/// <summary>Naming override (order must match <see cref="NamingPolicyKind"/> + 1).</summary>
public enum NamingOption
{
    ProfileDefault,
    Unchanged,
    CamelCase,
    SnakeCaseLower,
    SnakeCaseUpper,
    KebabCaseLower,
    KebabCaseUpper,
}

public enum EnumOption
{
    ProfileDefault,
    Numbers,
    Strings,
}

public enum IgnoreOption
{
    ProfileDefault,
    Never,
    WhenWritingNull,
    WhenWritingDefault,
}

public enum ReferenceOption
{
    ProfileDefault,
    None,
    Preserve,
    IgnoreCycles,
}
