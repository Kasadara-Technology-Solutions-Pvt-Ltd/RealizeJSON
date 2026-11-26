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
        store.Write(SettingsSerializer.StoreKey, SettingsSerializer.ToJson(settings));
        SettingsSaved?.Invoke(this, settings);
    }
}
