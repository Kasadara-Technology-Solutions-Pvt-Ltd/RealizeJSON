using System;
using System.Collections.Generic;

namespace RealizeJSON.Core;

/// <summary>Factory for documented serializer presets.</summary>
public static class ProfilePresets
{
    /// <summary>
    /// Creates a preset. System.Text.Json library defaults match <c>new JsonSerializerOptions()</c>; web defaults match
    /// <c>JsonSerializerDefaults.Web</c> as used by ASP.NET Core. Newtonsoft.Json library defaults match
    /// <c>new JsonSerializerSettings()</c>; web defaults match <c>AddNewtonsoftJson()</c> (DefaultContractResolver with
    /// CamelCaseNamingStrategy that does not process dictionary keys).
    /// </summary>
    public static SerializationProfile Create(SerializerKind serializer, ProfilePreset preset)
    {
        var web = preset == ProfilePreset.AspNetCoreWeb;
        return new SerializationProfile
        {
            Serializer = serializer,
            Preset = preset,
            PropertyNaming = web ? NamingPolicyKind.CamelCase : NamingPolicyKind.Unchanged,
            DictionaryKeyNaming = NamingPolicyKind.Unchanged,
            Enums = EnumRepresentation.Numbers,
            EnumNaming = NamingPolicyKind.Unchanged,
            IgnoreCondition = DefaultIgnoreCondition.Never,
            WriteIndented = true,
            SerializerVersion = serializer == SerializerKind.SystemTextJson ? "8.0" : "13.0",
        };
    }
}

/// <summary>Manually selected overrides; null means "not overridden".</summary>
public sealed record ProfileOverrides
{
    public NamingPolicyKind? PropertyNaming { get; init; }
    public NamingPolicyKind? DictionaryKeyNaming { get; init; }
    public EnumRepresentation? Enums { get; init; }
    public NamingPolicyKind? EnumNaming { get; init; }
    public DefaultIgnoreCondition? IgnoreCondition { get; init; }
    public bool? IncludeFields { get; init; }
    public bool? IgnoreReadOnlyProperties { get; init; }
    public bool? IgnoreReadOnlyFields { get; init; }
    public bool? WriteNumbersAsStrings { get; init; }
    public ReferenceHandlingMode? ReferenceHandling { get; init; }
    public bool? OverrideSpecifiedNames { get; init; }
    public bool? WriteIndented { get; init; }

    public static ProfileOverrides None { get; } = new();
}

/// <summary>Settings detected statically from project configuration code.</summary>
public sealed record DetectedConfiguration
{
    public SerializerKind Serializer { get; init; }

    /// <summary>Short description of where the configuration was found, e.g. "Program.cs: AddJsonOptions".</summary>
    public string Source { get; init; } = string.Empty;

    public SourceLocation? Location { get; init; }

    /// <summary>True when ASP.NET Core web defaults are the base for this configuration.</summary>
    public bool IsWebHost { get; init; }

    public ProfileOverrides Settings { get; init; } = ProfileOverrides.None;

    /// <summary>Settings that are set by code that cannot be evaluated statically.</summary>
    public IReadOnlyList<string> UnknownSettings { get; init; } = Array.Empty<string>();
}

/// <summary>Applies profile precedence: manual overrides &gt; detected settings &gt; preset defaults.</summary>
public static class ProfileResolver
{
    public static SerializationProfile Resolve(
        SerializerKind serializer,
        ProfilePreset preset,
        DetectedConfiguration? detected,
        ProfileOverrides? manual)
    {
        var effectivePreset = preset;
        var profile = ProfilePresets.Create(serializer, preset == ProfilePreset.Custom ? ProfilePreset.LibraryDefaults : preset) with { Preset = effectivePreset };
        var provenance = new Dictionary<string, SettingOrigin>(StringComparer.Ordinal);

        if (detected is not null && detected.Serializer == serializer)
        {
            profile = Apply(profile, detected.Settings, SettingOrigin.Detected, provenance);
            foreach (var unknown in detected.UnknownSettings)
            {
                provenance[unknown] = SettingOrigin.Unknown;
            }
        }

        if (manual is not null)
        {
            profile = Apply(profile, manual, SettingOrigin.Manual, provenance);
        }

        return profile with { Provenance = provenance };
    }

    private static SerializationProfile Apply(SerializationProfile p, ProfileOverrides o, SettingOrigin origin, Dictionary<string, SettingOrigin> provenance)
    {
        void Mark(bool set, string name)
        {
            if (set)
            {
                provenance[name] = origin;
            }
        }

        Mark(o.PropertyNaming.HasValue, ProfileSettings.PropertyNaming);
        Mark(o.DictionaryKeyNaming.HasValue, ProfileSettings.DictionaryKeyNaming);
        Mark(o.Enums.HasValue, ProfileSettings.Enums);
        Mark(o.EnumNaming.HasValue, ProfileSettings.EnumNaming);
        Mark(o.IgnoreCondition.HasValue, ProfileSettings.IgnoreCondition);
        Mark(o.IncludeFields.HasValue, ProfileSettings.IncludeFields);
        Mark(o.IgnoreReadOnlyProperties.HasValue, ProfileSettings.IgnoreReadOnlyProperties);
        Mark(o.IgnoreReadOnlyFields.HasValue, ProfileSettings.IgnoreReadOnlyFields);
        Mark(o.WriteNumbersAsStrings.HasValue, ProfileSettings.WriteNumbersAsStrings);
        Mark(o.ReferenceHandling.HasValue, ProfileSettings.ReferenceHandling);
        Mark(o.OverrideSpecifiedNames.HasValue, ProfileSettings.OverrideSpecifiedNames);
        Mark(o.WriteIndented.HasValue, ProfileSettings.WriteIndented);

        return p with
        {
            PropertyNaming = o.PropertyNaming ?? p.PropertyNaming,
            DictionaryKeyNaming = o.DictionaryKeyNaming ?? p.DictionaryKeyNaming,
            Enums = o.Enums ?? p.Enums,
            EnumNaming = o.EnumNaming ?? p.EnumNaming,
            IgnoreCondition = o.IgnoreCondition ?? p.IgnoreCondition,
            IncludeFields = o.IncludeFields ?? p.IncludeFields,
            IgnoreReadOnlyProperties = o.IgnoreReadOnlyProperties ?? p.IgnoreReadOnlyProperties,
            IgnoreReadOnlyFields = o.IgnoreReadOnlyFields ?? p.IgnoreReadOnlyFields,
            WriteNumbersAsStrings = o.WriteNumbersAsStrings ?? p.WriteNumbersAsStrings,
            ReferenceHandling = o.ReferenceHandling ?? p.ReferenceHandling,
            OverrideSpecifiedNames = o.OverrideSpecifiedNames ?? p.OverrideSpecifiedNames,
            WriteIndented = o.WriteIndented ?? p.WriteIndented,
        };
    }
}
