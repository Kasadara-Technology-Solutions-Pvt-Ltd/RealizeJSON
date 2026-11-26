using System;
using System.Globalization;
using RealizeJSON.Core.Contracts;
using RealizeJSON.Core.Json;
using RealizeJSON.Core.Samples;

namespace RealizeJSON.Serialization.SystemTextJson;

/// <summary>Scalar formatting matching System.Text.Json's default converters and Utf8JsonWriter.</summary>
public sealed class SystemTextJsonFormatter : IJsonValueFormatter
{
    public static SystemTextJsonFormatter Instance { get; } = new();

    public JsonEscaping Escaping => JsonEscaping.SystemTextJsonDefault;

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
                return new JsonStringNode(Convert.ToBase64String((byte[])value.Value), escape: false);
            case PrimitiveKind.DateTime:
                return new JsonStringNode(ScalarFormatting.FormatDateTime((DateTime)value.Value), escape: false);
            case PrimitiveKind.DateTimeOffset:
                return new JsonStringNode(ScalarFormatting.FormatDateTimeOffset((DateTimeOffset)value.Value), escape: false);
            case PrimitiveKind.DateOnly:
                return new JsonStringNode(ScalarFormatting.FormatDateOnly((DateOnlyValue)value.Value), escape: false);
            case PrimitiveKind.TimeOnly:
                return new JsonStringNode(ScalarFormatting.FormatTimeOnly((TimeOnlyValue)value.Value), escape: false);
            case PrimitiveKind.TimeSpan:
                return new JsonStringNode(ScalarFormatting.FormatTimeSpan((TimeSpan)value.Value), escape: false);
            case PrimitiveKind.Guid:
                return new JsonStringNode(((Guid)value.Value).ToString("D"), escape: false);
            default:
                var text = FormatNumber(value.Value);
                return numberAsString ? new JsonStringNode(text) : new JsonNumberNode(text);
        }
    }

    public string FormatKey(ScalarSampleValue value) => FormatScalar(value, numberAsString: false) switch
    {
        JsonStringNode s => s.Value,
        JsonNumberNode n => n.Text,
        JsonBooleanNode b => b.Value ? "true" : "false",
        _ => string.Empty,
    };

    public string? FormatEnumName(EnumContract contract, ulong rawValue) => ScalarFormatting.FormatEnumName(contract, rawValue);

    public string FormatEnumKey(EnumContract contract, ulong rawValue) =>
        ScalarFormatting.FormatEnumName(contract, rawValue) ?? FormatEnumNumber(contract, rawValue);

    internal static string FormatEnumNumber(EnumContract contract, ulong rawValue) => contract.IsSigned
        ? unchecked((long)rawValue).ToString(CultureInfo.InvariantCulture)
        : rawValue.ToString(CultureInfo.InvariantCulture);

    internal static string FormatNumber(object value) => value switch
    {
        long l => l.ToString(CultureInfo.InvariantCulture),
        ulong u => u.ToString(CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        string s => s,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0",
    };
}
