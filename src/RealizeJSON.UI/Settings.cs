using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RealizeJSON.Core;
using RealizeJSON.Core.Json;
using RealizeJSON.Serialization;

namespace RealizeJSON.UI;

/// <summary>Per-user settings (persisted) and optional solution profile values.</summary>
public sealed record UserSettings
{
    public const int CurrentSchemaVersion = 2;

    public SerializerKind Serializer { get; init; } = SerializerKind.SystemTextJson;

    public ProfilePreset Preset { get; init; } = ProfilePreset.AspNetCoreWeb;

    public SampleMode SampleMode { get; init; } = SampleMode.SampleValues;

    public int CollectionCount { get; init; } = 1;

    public int DebounceMilliseconds { get; init; } = 350;

    public int MaxDepth { get; init; } = 8;

    public int MaxNodes { get; init; } = 2000;

    public int MaxBytes { get; init; } = 256 * 1024;

    public bool NullableMembersAsNull { get; init; }

    public bool UseNameHeuristics { get; init; } = true;

    public bool SensitiveNameWarnings { get; init; } = true;

    public ConfigurationMode ConfigurationMode { get; init; } = ConfigurationMode.Detect;

    public bool WordWrap { get; init; }

    public ProfileOverrides Overrides { get; init; } = ProfileOverrides.None;

    public PreviewOptions ToOptions(string? variant) => new PreviewOptions
    {
        Mode = SampleMode,
        CollectionCount = CollectionCount,
        MaxDepth = MaxDepth,
        MaxNodes = MaxNodes,
        MaxBytes = MaxBytes,
        NullableMembersAsNull = NullableMembersAsNull,
        UseNameHeuristics = UseNameHeuristics,
        PolymorphicVariant = string.IsNullOrEmpty(variant) ? null : variant,
    }.Normalize();

    public UserSettings Normalize() => this with
    {
        CollectionCount = Math.Max(0, Math.Min(PreviewOptions.MaxCollectionCount, CollectionCount)),
        DebounceMilliseconds = Math.Max(0, Math.Min(5000, DebounceMilliseconds)),
        MaxDepth = Math.Max(1, Math.Min(64, MaxDepth)),
        MaxNodes = Math.Max(10, Math.Min(1_000_000, MaxNodes)),
        MaxBytes = Math.Max(1024, Math.Min(16 * 1024 * 1024, MaxBytes)),
    };
}

/// <summary>Result of reading settings.</summary>
public sealed record SettingsReadResult(UserSettings Settings, bool WasMigrated, string? Warning);

/// <summary>
/// JSON persistence with schema versioning and migration. Malformed input never throws: defaults are returned with a
/// warning. Unknown keys are ignored; enum values are stored by name.
/// </summary>
public static class SettingsSerializer
{
    public const string StoreKey = "RealizeJSON.Settings";

    public static string ToJson(UserSettings settings)
    {
        var root = new JsonObjectNode();
        void Add(string name, JsonNodeModel value) => root.Properties.Add(new JsonPropertyNode(name, value, null));
        Add("schemaVersion", Number(UserSettings.CurrentSchemaVersion));
        Add("serializer", new JsonStringNode(settings.Serializer.ToString()));
        Add("preset", new JsonStringNode(settings.Preset.ToString()));
        Add("sampleMode", new JsonStringNode(settings.SampleMode.ToString()));
        Add("collectionCount", Number(settings.CollectionCount));
        Add("debounceMs", Number(settings.DebounceMilliseconds));
        Add("maxDepth", Number(settings.MaxDepth));
        Add("maxNodes", Number(settings.MaxNodes));
        Add("maxBytes", Number(settings.MaxBytes));
        Add("nullableMembersAsNull", new JsonBooleanNode(settings.NullableMembersAsNull));
        Add("useNameHeuristics", new JsonBooleanNode(settings.UseNameHeuristics));
        Add("sensitiveNameWarnings", new JsonBooleanNode(settings.SensitiveNameWarnings));
        Add("configurationMode", new JsonStringNode(settings.ConfigurationMode.ToString()));
        Add("wordWrap", new JsonBooleanNode(settings.WordWrap));
        Add("overrides", OverridesToJson(settings.Overrides));
        return JsonTextWriter.Write(root, indented: true, JsonEscaping.NewtonsoftDefault).Text;
    }

    public static JsonObjectNode OverridesToJson(ProfileOverrides o)
    {
        var node = new JsonObjectNode();
        void Add(string name, object? value)
        {
            switch (value)
            {
                case null:
                    return;
                case bool b:
                    node.Properties.Add(new JsonPropertyNode(name, new JsonBooleanNode(b), null));
                    return;
                default:
                    node.Properties.Add(new JsonPropertyNode(name, new JsonStringNode(value.ToString()!), null));
                    return;
            }
        }

        Add("propertyNaming", o.PropertyNaming);
        Add("dictionaryKeyNaming", o.DictionaryKeyNaming);
        Add("enums", o.Enums);
        Add("enumNaming", o.EnumNaming);
        Add("ignoreCondition", o.IgnoreCondition);
        Add("includeFields", o.IncludeFields);
        Add("ignoreReadOnlyProperties", o.IgnoreReadOnlyProperties);
        Add("ignoreReadOnlyFields", o.IgnoreReadOnlyFields);
        Add("writeNumbersAsStrings", o.WriteNumbersAsStrings);
        Add("referenceHandling", o.ReferenceHandling);
        Add("overrideSpecifiedNames", o.OverrideSpecifiedNames);
        Add("writeIndented", o.WriteIndented);
        return node;
    }

    public static SettingsReadResult FromJson(string? json)
    {
        var defaults = new UserSettings();
        if (string.IsNullOrWhiteSpace(json))
        {
            return new SettingsReadResult(defaults, false, null);
        }

        JsonObjectNode root;
        try
        {
            root = JsonTextReader.Parse(json!) as JsonObjectNode ?? throw new JsonReadException("Settings must be a JSON object", 0);
        }
        catch (JsonReadException ex)
        {
            return new SettingsReadResult(defaults, false, "Settings were malformed and have been reset to defaults: " + ex.Message);
        }

        var map = root.Properties.GroupBy(p => p.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal);
        var version = Int(map, "schemaVersion") ?? 1;
        var migrated = false;
        if (version < 2)
        {
            // v1 stored the debounce as "debounce" and the preset as a boolean "webDefaults".
            if (map.TryGetValue("debounce", out var oldDebounce) && !map.ContainsKey("debounceMs"))
            {
                map["debounceMs"] = oldDebounce;
            }

            if (map.TryGetValue("webDefaults", out var web) && web is JsonBooleanNode b && !map.ContainsKey("preset"))
            {
                map["preset"] = new JsonStringNode(b.Value ? nameof(ProfilePreset.AspNetCoreWeb) : nameof(ProfilePreset.LibraryDefaults));
            }

            migrated = true;
        }
        else if (version > UserSettings.CurrentSchemaVersion)
        {
            return new SettingsReadResult(defaults, false, $"Settings schema version {version} is newer than supported ({UserSettings.CurrentSchemaVersion}); defaults are used.");
        }

        var settings = new UserSettings
        {
            Serializer = Enum(map, "serializer", defaults.Serializer),
            Preset = Enum(map, "preset", defaults.Preset),
            SampleMode = Enum(map, "sampleMode", defaults.SampleMode),
            CollectionCount = Int(map, "collectionCount") ?? defaults.CollectionCount,
            DebounceMilliseconds = Int(map, "debounceMs") ?? defaults.DebounceMilliseconds,
            MaxDepth = Int(map, "maxDepth") ?? defaults.MaxDepth,
            MaxNodes = Int(map, "maxNodes") ?? defaults.MaxNodes,
            MaxBytes = Int(map, "maxBytes") ?? defaults.MaxBytes,
            NullableMembersAsNull = Bool(map, "nullableMembersAsNull") ?? defaults.NullableMembersAsNull,
            UseNameHeuristics = Bool(map, "useNameHeuristics") ?? defaults.UseNameHeuristics,
            SensitiveNameWarnings = Bool(map, "sensitiveNameWarnings") ?? defaults.SensitiveNameWarnings,
            ConfigurationMode = Enum(map, "configurationMode", defaults.ConfigurationMode),
            WordWrap = Bool(map, "wordWrap") ?? defaults.WordWrap,
            Overrides = map.TryGetValue("overrides", out var o) && o is JsonObjectNode overrides ? ReadOverrides(overrides) : ProfileOverrides.None,
        }.Normalize();

        return new SettingsReadResult(settings, migrated, null);
    }

    public static ProfileOverrides ReadOverrides(JsonObjectNode node)
    {
        var map = node.Properties.GroupBy(p => p.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal);
        return new ProfileOverrides
        {
            PropertyNaming = NullableEnum<NamingPolicyKind>(map, "propertyNaming"),
            DictionaryKeyNaming = NullableEnum<NamingPolicyKind>(map, "dictionaryKeyNaming"),
            Enums = NullableEnum<EnumRepresentation>(map, "enums"),
            EnumNaming = NullableEnum<NamingPolicyKind>(map, "enumNaming"),
            IgnoreCondition = NullableEnum<DefaultIgnoreCondition>(map, "ignoreCondition"),
            IncludeFields = Bool(map, "includeFields"),
            IgnoreReadOnlyProperties = Bool(map, "ignoreReadOnlyProperties"),
            IgnoreReadOnlyFields = Bool(map, "ignoreReadOnlyFields"),
            WriteNumbersAsStrings = Bool(map, "writeNumbersAsStrings"),
            ReferenceHandling = NullableEnum<ReferenceHandlingMode>(map, "referenceHandling"),
            OverrideSpecifiedNames = Bool(map, "overrideSpecifiedNames"),
            WriteIndented = Bool(map, "writeIndented"),
        };
    }

    private static JsonNumberNode Number(int value) => new(value.ToString(CultureInfo.InvariantCulture));

    private static int? Int(Dictionary<string, JsonNodeModel> map, string key) =>
        map.TryGetValue(key, out var v) && v is JsonNumberNode n && int.TryParse(n.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    private static bool? Bool(Dictionary<string, JsonNodeModel> map, string key) =>
        map.TryGetValue(key, out var v) && v is JsonBooleanNode b ? b.Value : null;

    private static T Enum<T>(Dictionary<string, JsonNodeModel> map, string key, T fallback)
        where T : struct => NullableEnum<T>(map, key) ?? fallback;

    private static T? NullableEnum<T>(Dictionary<string, JsonNodeModel> map, string key)
        where T : struct =>
        map.TryGetValue(key, out var v) && v is JsonStringNode s && System.Enum.TryParse<T>(s.Value, ignoreCase: false, out var parsed) && System.Enum.IsDefined(typeof(T), parsed)
            ? parsed
            : null;
}

/// <summary>
/// Optional solution-scoped profile stored as <c>.realizejson.json</c> next to the solution. Read automatically;
/// written only by an explicit user command. Project files are never modified.
/// </summary>
public static class SolutionProfile
{
    public const string FileName = ".realizejson.json";

    public static string ToJson(SerializerKind serializer, ProfilePreset preset, ProfileOverrides overrides)
    {
        var root = new JsonObjectNode();
        root.Properties.Add(new JsonPropertyNode("schemaVersion", new JsonNumberNode("1"), null));
        root.Properties.Add(new JsonPropertyNode("serializer", new JsonStringNode(serializer.ToString()), null));
        root.Properties.Add(new JsonPropertyNode("preset", new JsonStringNode(preset.ToString()), null));
        root.Properties.Add(new JsonPropertyNode("overrides", SettingsSerializer.OverridesToJson(overrides), null));
        return JsonTextWriter.Write(root, indented: true, JsonEscaping.NewtonsoftDefault).Text;
    }

    /// <summary>Applies a solution profile over user settings; returns the input and a warning when malformed.</summary>
    public static (UserSettings Settings, string? Warning) Apply(UserSettings settings, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (settings, null);
        }

        try
        {
            if (JsonTextReader.Parse(json!) is not JsonObjectNode root)
            {
                return (settings, FileName + " must contain a JSON object; it was ignored.");
            }

            var map = root.Properties.ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
            var result = settings;
            if (map.TryGetValue("serializer", out var s) && s is JsonStringNode sn && System.Enum.TryParse<SerializerKind>(sn.Value, out var serializer))
            {
                result = result with { Serializer = serializer };
            }

            if (map.TryGetValue("preset", out var p) && p is JsonStringNode pn && System.Enum.TryParse<ProfilePreset>(pn.Value, out var preset))
            {
                result = result with { Preset = preset };
            }

            if (map.TryGetValue("overrides", out var o) && o is JsonObjectNode on)
            {
                result = result with { Overrides = SettingsSerializer.ReadOverrides(on) };
            }

            return (result, null);
        }
        catch (JsonReadException ex)
        {
            return (settings, FileName + " is malformed and was ignored: " + ex.Message);
        }
    }
}
