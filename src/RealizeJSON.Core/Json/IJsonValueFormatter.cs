using RealizeJSON.Core.Contracts;
using RealizeJSON.Core.Samples;

namespace RealizeJSON.Core.Json;

/// <summary>String escaping behavior of a serializer's default writer.</summary>
public enum JsonEscaping
{
    /// <summary>System.Text.Json JavaScriptEncoder.Default: HTML-sensitive and non-ASCII characters are escaped.</summary>
    SystemTextJsonDefault,

    /// <summary>Newtonsoft.Json StringEscapeHandling.Default: only quotes, backslash, control and line-separator characters.</summary>
    NewtonsoftDefault,
}

/// <summary>Serializer-specific scalar representation.</summary>
public interface IJsonValueFormatter
{
    JsonEscaping Escaping { get; }

    /// <summary>Formats a scalar CLR value. <paramref name="numberAsString"/> requests quoted numbers.</summary>
    JsonNodeModel FormatScalar(ScalarSampleValue value, bool numberAsString);

    /// <summary>Formats a scalar used as a dictionary key (before key naming policy).</summary>
    string FormatKey(ScalarSampleValue value);

    /// <summary>Formats an enum value as a string using member wire names, or returns null when no string form exists.</summary>
    string? FormatEnumName(EnumContract contract, ulong rawValue);

    /// <summary>Formats an enum value used as a dictionary key.</summary>
    string FormatEnumKey(EnumContract contract, ulong rawValue);
}
