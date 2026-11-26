using System;
using System.Globalization;
using RealizeJSON.Core.Contracts;
using RealizeJSON.Core.Json;
using RealizeJSON.Core.Samples;

namespace RealizeJSON.Serialization.NewtonsoftJson;

/// <summary>Scalar formatting matching Newtonsoft.Json's JsonTextWriter defaults (IsoDateFormat, RoundtripKind).</summary>
public sealed class NewtonsoftJsonFormatter : IJsonValueFormatter
{
    public static NewtonsoftJsonFormatter Instance { get; } = new();

    public JsonEscaping Escaping => JsonEscaping.NewtonsoftDefault;

    public JsonNodeModel FormatScalar(ScalarSampleValue value, bool numberAsString)
    {
        switch (value.Kind)
        {
            case PrimitiveKind.Boolean:
                return new JsonBooleanNode((bool)value.Value);
            case PrimitiveKind.String:
            case PrimitiveKind.Uri:
            case PrimitiveKind.Version:
                return new JsonStringNode((string)value.Value);
            case PrimitiveKind.Char:
                return new JsonStringNode(((char)value.Value).ToString());
            case PrimitiveKind.ByteArray:
                return new JsonStringNode(Convert.ToBase64String((byte[])value.Value));
            case PrimitiveKind.DateTime:
                return new JsonStringNode(ScalarFormatting.FormatDateTime((DateTime)value.Value));
            case PrimitiveKind.DateTimeOffset:
                return new JsonStringNode(ScalarFormatting.FormatDateTimeOffset((DateTimeOffset)value.Value));
            case PrimitiveKind.DateOnly:
                return new JsonStringNode(ScalarFormatting.FormatDateOnly((DateOnlyValue)value.Value));
            case PrimitiveKind.TimeOnly:
                return new JsonStringNode(ScalarFormatting.FormatTimeOnly((TimeOnlyValue)value.Value));
            case PrimitiveKind.TimeSpan:
                return new JsonStringNode(ScalarFormatting.FormatTimeSpan((TimeSpan)value.Value));
            case PrimitiveKind.Guid:
                return new JsonStringNode(((Guid)value.Value).ToString("D"));
            case PrimitiveKind.Half:
                // Newtonsoft.Json has no Half support; it falls back to the type's string conversion.
                return new JsonStringNode(((double)value.Value).ToString("R", CultureInfo.InvariantCulture));
            default:
                return new JsonNumberNode(FormatNumber(value.Value));
        }
    }

    public string FormatKey(ScalarSampleValue value) => value.Value switch
    {
        bool b => b ? "True" : "False",
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        _ => FormatScalar(value, false) switch
        {
            JsonStringNode s => s.Value,
            JsonNumberNode n => n.Text,
            _ => string.Empty,
        },
    };

    public string? FormatEnumName(EnumContract contract, ulong rawValue) => ScalarFormatting.FormatEnumName(contract, rawValue);

    /// <summary>Newtonsoft writes enum dictionary keys with declared (or [EnumMember]) names, ignoring converters.</summary>
    public string FormatEnumKey(EnumContract contract, ulong rawValue) =>
        ScalarFormatting.FormatEnumName(contract, rawValue, keyNames: true)
        ?? (contract.IsSigned ? unchecked((long)rawValue).ToString(CultureInfo.InvariantCulture) : rawValue.ToString(CultureInfo.InvariantCulture));

    private static string FormatNumber(object value) => value switch
    {
        long l => l.ToString(CultureInfo.InvariantCulture),
        ulong u => u.ToString(CultureInfo.InvariantCulture),
        float f => EnsureDecimalPlace(f.ToString("R", CultureInfo.InvariantCulture)),
        double d => EnsureDecimalPlace(d.ToString("R", CultureInfo.InvariantCulture)),
        decimal m => EnsureDecimalPlace(m.ToString(CultureInfo.InvariantCulture)),
        string s => s,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0",
    };

    /// <summary>Newtonsoft writes floating-point and decimal values with at least one decimal place.</summary>
    private static string EnsureDecimalPlace(string text) =>
        text.IndexOf('.') >= 0 || text.IndexOf('E') >= 0 || text.IndexOf('e') >= 0 || text == "NaN" || text.EndsWith("Infinity", StringComparison.Ordinal)
            ? text
            : text + ".0";
}
