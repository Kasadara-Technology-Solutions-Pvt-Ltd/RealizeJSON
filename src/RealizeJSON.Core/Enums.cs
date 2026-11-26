namespace RealizeJSON.Core;

/// <summary>Confidence classification applied at document and member level.</summary>
public enum Classification
{
    /// <summary>The relevant contract rule is understood under the selected profile.</summary>
    Resolved = 0,

    /// <summary>A synthetic value or statically inferred behavior is used.</summary>
    Estimated = 1,

    /// <summary>Inclusion or representation depends on runtime values or settings.</summary>
    Conditional = 2,

    /// <summary>A rule or type cannot currently be modeled.</summary>
    Unsupported = 3,

    /// <summary>Source symbols or configuration cannot be resolved.</summary>
    Unresolved = 4,
}

/// <summary>Supported serializers.</summary>
public enum SerializerKind
{
    SystemTextJson = 0,
    NewtonsoftJson = 1,
}

/// <summary>Profile presets.</summary>
public enum ProfilePreset
{
    /// <summary>Standalone serializer defaults (no ASP.NET Core conventions).</summary>
    LibraryDefaults = 0,

    /// <summary>ASP.NET Core web defaults for the selected serializer.</summary>
    AspNetCoreWeb = 1,

    /// <summary>User-defined preview profile.</summary>
    Custom = 2,
}

/// <summary>Member and dictionary-key naming policies.</summary>
public enum NamingPolicyKind
{
    /// <summary>Identifiers are written exactly as declared.</summary>
    Unchanged = 0,
    CamelCase = 1,
    SnakeCaseLower = 2,
    SnakeCaseUpper = 3,
    KebabCaseLower = 4,
    KebabCaseUpper = 5,
}

/// <summary>Enum representation.</summary>
public enum EnumRepresentation
{
    Numbers = 0,
    Strings = 1,
}

/// <summary>Global write-time ignore condition.</summary>
public enum DefaultIgnoreCondition
{
    Never = 0,
    WhenWritingNull = 1,
    WhenWritingDefault = 2,
}

/// <summary>Built-in reference handling modes.</summary>
public enum ReferenceHandlingMode
{
    /// <summary>No reference handling (cycles fail at runtime).</summary>
    None = 0,

    /// <summary>System.Text.Json ReferenceHandler.Preserve / Newtonsoft PreserveReferencesHandling.Objects.</summary>
    Preserve = 1,

    /// <summary>System.Text.Json ReferenceHandler.IgnoreCycles / Newtonsoft ReferenceLoopHandling.Ignore.</summary>
    IgnoreCycles = 2,
}

/// <summary>How sample values are produced.</summary>
public enum SampleMode
{
    /// <summary>Deterministic illustrative values.</summary>
    SampleValues = 0,

    /// <summary>Synthetic CLR default values (not constructor or initializer simulation).</summary>
    ClrDefaults = 1,
}

/// <summary>Where a resolved profile setting came from.</summary>
public enum SettingOrigin
{
    PresetDefault = 0,
    Detected = 1,
    Inferred = 2,
    Manual = 3,
    Unknown = 4,
}

/// <summary>Diagnostic severity.</summary>
public enum DiagnosticSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>Overall preview state shown in the UI.</summary>
public enum PreviewState
{
    Empty = 0,
    Analyzing = 1,
    Live = 2,
    Stale = 3,
    Partial = 4,
    Unsupported = 5,
    Error = 6,
    Suspended = 7,
}
