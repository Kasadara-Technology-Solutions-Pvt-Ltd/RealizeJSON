using System;
using System.Collections.Generic;

namespace RealizeJSON.Core;

/// <summary>
/// Immutable serialization profile. Only settings meaningful for <see cref="Serializer"/> are applied;
/// the others are ignored by that serializer's rule provider.
/// </summary>
public sealed record SerializationProfile
{
    public SerializerKind Serializer { get; init; } = SerializerKind.SystemTextJson;

    public ProfilePreset Preset { get; init; } = ProfilePreset.LibraryDefaults;

    public NamingPolicyKind PropertyNaming { get; init; } = NamingPolicyKind.Unchanged;

    /// <summary>Dictionary key policy (STJ DictionaryKeyPolicy / Newtonsoft ProcessDictionaryKeys with the naming strategy).</summary>
    public NamingPolicyKind DictionaryKeyNaming { get; init; } = NamingPolicyKind.Unchanged;

    public EnumRepresentation Enums { get; init; } = EnumRepresentation.Numbers;

    /// <summary>Naming policy applied to enum member names when written as strings.</summary>
    public NamingPolicyKind EnumNaming { get; init; } = NamingPolicyKind.Unchanged;

    public DefaultIgnoreCondition IgnoreCondition { get; init; } = DefaultIgnoreCondition.Never;

    /// <summary>System.Text.Json IncludeFields.</summary>
    public bool IncludeFields { get; init; }

    /// <summary>System.Text.Json IgnoreReadOnlyProperties.</summary>
    public bool IgnoreReadOnlyProperties { get; init; }

    /// <summary>System.Text.Json IgnoreReadOnlyFields.</summary>
    public bool IgnoreReadOnlyFields { get; init; }

    /// <summary>System.Text.Json JsonNumberHandling.WriteAsString applied globally.</summary>
    public bool WriteNumbersAsStrings { get; init; }

    public ReferenceHandlingMode ReferenceHandling { get; init; } = ReferenceHandlingMode.None;

    /// <summary>Newtonsoft NamingStrategy.OverrideSpecifiedNames.</summary>
    public bool OverrideSpecifiedNames { get; init; }

    public bool WriteIndented { get; init; } = true;

    /// <summary>Serializer version the rules are evaluated against (major.minor), e.g. "8.0" or "13.0".</summary>
    public string SerializerVersion { get; init; } = "8.0";

    /// <summary>Origin of each setting, keyed by setting name.</summary>
    public IReadOnlyDictionary<string, SettingOrigin> Provenance { get; init; } = EmptyProvenance;

    /// <summary>Human-readable profile name for the preview label.</summary>
    public string DisplayName => (Serializer == SerializerKind.SystemTextJson ? "System.Text.Json" : "Newtonsoft.Json")
        + " · " + (Preset switch
        {
            ProfilePreset.LibraryDefaults => "Library defaults",
            ProfilePreset.AspNetCoreWeb => "ASP.NET Core web defaults",
            _ => "Custom",
        });

    private static readonly IReadOnlyDictionary<string, SettingOrigin> EmptyProvenance =
        new Dictionary<string, SettingOrigin>(StringComparer.Ordinal);

    /// <summary>Returns the origin recorded for a setting.</summary>
    public SettingOrigin OriginOf(string setting) =>
        Provenance.TryGetValue(setting, out var origin) ? origin : SettingOrigin.PresetDefault;
}

/// <summary>Names of profile settings used for provenance and overrides.</summary>
public static class ProfileSettings
{
    public const string PropertyNaming = nameof(SerializationProfile.PropertyNaming);
    public const string DictionaryKeyNaming = nameof(SerializationProfile.DictionaryKeyNaming);
    public const string Enums = nameof(SerializationProfile.Enums);
    public const string EnumNaming = nameof(SerializationProfile.EnumNaming);
    public const string IgnoreCondition = nameof(SerializationProfile.IgnoreCondition);
    public const string IncludeFields = nameof(SerializationProfile.IncludeFields);
    public const string IgnoreReadOnlyProperties = nameof(SerializationProfile.IgnoreReadOnlyProperties);
    public const string IgnoreReadOnlyFields = nameof(SerializationProfile.IgnoreReadOnlyFields);
    public const string WriteNumbersAsStrings = nameof(SerializationProfile.WriteNumbersAsStrings);
    public const string ReferenceHandling = nameof(SerializationProfile.ReferenceHandling);
    public const string OverrideSpecifiedNames = nameof(SerializationProfile.OverrideSpecifiedNames);
    public const string WriteIndented = nameof(SerializationProfile.WriteIndented);

    /// <summary>Settings that are meaningful for the given serializer.</summary>
    public static IReadOnlyList<string> For(SerializerKind serializer) => serializer == SerializerKind.SystemTextJson
        ? new[] { PropertyNaming, DictionaryKeyNaming, Enums, EnumNaming, IgnoreCondition, IncludeFields, IgnoreReadOnlyProperties, IgnoreReadOnlyFields, WriteNumbersAsStrings, ReferenceHandling, WriteIndented }
        : new[] { PropertyNaming, DictionaryKeyNaming, Enums, EnumNaming, IgnoreCondition, ReferenceHandling, OverrideSpecifiedNames, WriteIndented };

    /// <summary>Explains why a setting is not available for a serializer, or returns null when it is.</summary>
    public static string? ExplainUnsupported(SerializerKind serializer, string setting)
    {
        if (serializer == SerializerKind.NewtonsoftJson)
        {
            return setting switch
            {
                IncludeFields => "Newtonsoft.Json always serializes public fields; use [JsonObject(MemberSerialization.Fields)] or [JsonIgnore] to change field inclusion.",
                IgnoreReadOnlyProperties or IgnoreReadOnlyFields => "Newtonsoft.Json has no global read-only member switch; read-only members are serialized.",
                WriteNumbersAsStrings => "Newtonsoft.Json has no built-in number-as-string handling.",
                _ => null,
            };
        }

        return setting == OverrideSpecifiedNames
            ? "System.Text.Json never renames members that have [JsonPropertyName]."
            : null;
    }
}
