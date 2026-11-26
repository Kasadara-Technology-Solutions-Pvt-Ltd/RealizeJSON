using System;
using System.Globalization;
using RealizeJSON.Core.Contracts;
using RealizeJSON.Core.Samples;

namespace RealizeJSON.SampleData;

/// <summary>
/// Deterministic sample values. Values depend only on the primitive kind, the member/type names and the
/// variation index, so unchanged sources always produce identical previews. No external data or PII.
/// </summary>
public static class SampleHeuristics
{
    public static readonly DateTime BaseDate = new(2024, 1, 15, 9, 30, 0, DateTimeKind.Unspecified);

    private static readonly string[] TypeSuffixes = { "ViewModel", "Dto", "DTO", "Model", "Entity", "Request", "Response", "Command", "Query", "Event", "Record", "Item", "Vm" };

    public static ScalarSampleValue Default(PrimitiveKind kind)
    {
        object value = kind switch
        {
            PrimitiveKind.Boolean => false,
            PrimitiveKind.Char => '\0',
            PrimitiveKind.SByte or PrimitiveKind.Int16 or PrimitiveKind.Int32 or PrimitiveKind.Int64 => 0L,
            PrimitiveKind.Byte or PrimitiveKind.UInt16 or PrimitiveKind.UInt32 or PrimitiveKind.UInt64 => 0UL,
            PrimitiveKind.Single => 0f,
            PrimitiveKind.Half or PrimitiveKind.Double => 0d,
            PrimitiveKind.Decimal => 0m,
            PrimitiveKind.Int128 or PrimitiveKind.UInt128 or PrimitiveKind.BigInteger => "0",
            PrimitiveKind.DateTime => default(DateTime),
            PrimitiveKind.DateTimeOffset => default(DateTimeOffset),
            PrimitiveKind.DateOnly => new DateOnlyValue(1, 1, 1),
            PrimitiveKind.TimeOnly => new TimeOnlyValue(0),
            PrimitiveKind.TimeSpan => TimeSpan.Zero,
            PrimitiveKind.Guid => Guid.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Reference types have no non-null default."),
        };
        return new ScalarSampleValue(kind, value, isDefault: true);
    }

    /// <summary>True when the kind is a reference type (default is null).</summary>
    public static bool IsReferenceKind(PrimitiveKind kind) =>
        kind is PrimitiveKind.String or PrimitiveKind.Uri or PrimitiveKind.Version or PrimitiveKind.ByteArray;

    public static ScalarSampleValue Sample(PrimitiveKind kind, string? memberName, string? declaringTypeName, int variation, bool useNames)
    {
        var name = useNames ? (memberName ?? string.Empty) : string.Empty;
        var lower = name.ToLowerInvariant();
        object value = kind switch
        {
            PrimitiveKind.Boolean => variation % 2 == 0,
            PrimitiveKind.String => SampleString(name, lower, declaringTypeName, variation),
            PrimitiveKind.Char => (char)('A' + (variation % 26)),
            PrimitiveKind.SByte => (long)(7 + variation),
            PrimitiveKind.Byte => (ulong)(7 + variation),
            PrimitiveKind.Int16 => (long)(12 + variation),
            PrimitiveKind.UInt16 => (ulong)(12 + variation),
            PrimitiveKind.Int32 or PrimitiveKind.Int64 => SampleInteger(lower, variation),
            PrimitiveKind.UInt32 or PrimitiveKind.UInt64 => (ulong)SampleInteger(lower, variation),
            PrimitiveKind.Int128 or PrimitiveKind.UInt128 or PrimitiveKind.BigInteger => SampleInteger(lower, variation).ToString(CultureInfo.InvariantCulture),
            PrimitiveKind.Single => 1.5f + variation,
            PrimitiveKind.Half => 1.5d + variation,
            PrimitiveKind.Double => SampleDouble(lower, variation),
            PrimitiveKind.Decimal => SampleDecimal(lower, variation),
            PrimitiveKind.DateTime => BaseDate.AddDays(variation),
            PrimitiveKind.DateTimeOffset => new DateTimeOffset(BaseDate.AddDays(variation), TimeSpan.Zero),
            PrimitiveKind.DateOnly => ToDateOnly(BaseDate.AddDays(variation)),
            PrimitiveKind.TimeOnly => new TimeOnlyValue(new TimeSpan(9 + (variation % 14), 30, 0).Ticks),
            PrimitiveKind.TimeSpan => new TimeSpan(1 + variation, 30, 0),
            PrimitiveKind.Guid => SampleGuid(variation),
            PrimitiveKind.Uri => variation == 0 ? "https://example.com/" : "https://example.com/item/" + (variation + 1).ToString(CultureInfo.InvariantCulture),
            PrimitiveKind.Version => "1.0." + variation.ToString(CultureInfo.InvariantCulture),
            PrimitiveKind.ByteArray => new byte[] { 1, 2, (byte)(3 + variation) },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        return new ScalarSampleValue(kind, value, IsDefaultValue(value));
    }

    private static bool IsDefaultValue(object value) => value switch
    {
        bool b => !b,
        long l => l == 0,
        ulong u => u == 0,
        float f => f == 0f,
        double d => d == 0d,
        decimal m => m == 0m,
        char c => c == '\0',
        _ => false,
    };

    private static DateOnlyValue ToDateOnly(DateTime d) => new(d.Year, d.Month, d.Day);

    private static Guid SampleGuid(int variation)
    {
        var bytes = new Guid("3fa85f64-5717-4562-b3fc-2c963f66afa6").ToByteArray();
        bytes[15] = unchecked((byte)(bytes[15] + variation));
        return new Guid(bytes);
    }

    private static long SampleInteger(string lower, int variation)
    {
        long baseValue;
        if (lower == "id" || lower.EndsWith("id", StringComparison.Ordinal))
        {
            baseValue = 101;
        }
        else if (lower.Contains("age"))
        {
            baseValue = 30;
        }
        else if (lower.Contains("year"))
        {
            baseValue = 2024;
        }
        else if (lower.Contains("count") || lower.Contains("quantity") || lower == "qty")
        {
            baseValue = 3;
        }
        else if (lower.Contains("port"))
        {
            baseValue = 8080;
        }
        else
        {
            baseValue = 42;
        }

        return baseValue + variation;
    }

    private static double SampleDouble(string lower, int variation)
    {
        if (lower.StartsWith("lat", StringComparison.Ordinal))
        {
            return 47.6062 + variation;
        }

        if (lower.StartsWith("lon", StringComparison.Ordinal) || lower.StartsWith("lng", StringComparison.Ordinal))
        {
            return -122.3321 + variation;
        }

        return 12.5 + variation;
    }

    private static decimal SampleDecimal(string lower, int variation)
    {
        foreach (var money in new[] { "price", "amount", "total", "cost", "balance", "fee", "salary" })
        {
            if (lower.Contains(money))
            {
                return 19.99m + variation;
            }
        }

        return 12.75m + variation;
    }

    private static string SampleString(string name, string lower, string? declaringTypeName, int variation)
    {
        var suffix = variation == 0 ? string.Empty : " " + (variation + 1).ToString(CultureInfo.InvariantCulture);
        var n = (variation + 1).ToString(CultureInfo.InvariantCulture);
        if (lower.Length == 0)
        {
            return "sample" + suffix;
        }

        if (lower.Contains("email"))
        {
            return variation == 0 ? "user@example.com" : "user" + n + "@example.com";
        }

        if (lower.Contains("url") || lower.Contains("uri") || lower.Contains("website") || lower.Contains("link"))
        {
            return variation == 0 ? "https://example.com/" : "https://example.com/" + n;
        }

        if (lower.Contains("phone") || lower.Contains("mobile"))
        {
            return "+1-555-01" + (variation % 100).ToString("00", CultureInfo.InvariantCulture);
        }

        if (lower == "firstname" || lower == "givenname")
        {
            return "Alex" + suffix;
        }

        if (lower == "lastname" || lower == "surname" || lower == "familyname")
        {
            return "Morgan" + suffix;
        }

        if (lower == "name" || lower == "fullname" || lower == "displayname")
        {
            var typeName = StripSuffix(declaringTypeName);
            return (typeName.Length == 0 ? "Sample name" : "Sample " + typeName) + suffix;
        }

        if (lower.Contains("password") || lower.Contains("secret") || lower.Contains("token"))
        {
            return "sample-" + lower + suffix.Replace(" ", "-");
        }

        if (lower == "id" || lower.EndsWith("id", StringComparison.Ordinal))
        {
            return lower + "-" + (variation + 1).ToString("000", CultureInfo.InvariantCulture);
        }

        if (lower.Contains("currency"))
        {
            return variation == 0 ? "USD" : "EUR";
        }

        if (lower.Contains("culture") || lower.Contains("locale") || lower.Contains("language"))
        {
            return variation == 0 ? "en-US" : "fr-FR";
        }

        if (lower.Contains("zip") || lower.Contains("postal"))
        {
            return (12345 + variation).ToString(CultureInfo.InvariantCulture);
        }

        if (lower.Contains("street") || lower.Contains("address"))
        {
            return (123 + variation).ToString(CultureInfo.InvariantCulture) + " Sample Street";
        }

        if (lower.Contains("code"))
        {
            return "CODE-" + (variation + 1).ToString("000", CultureInfo.InvariantCulture);
        }

        return "Sample " + Humanize(name) + suffix;
    }

    private static string StripSuffix(string? typeName)
    {
        if (string.IsNullOrEmpty(typeName))
        {
            return string.Empty;
        }

        var t = typeName!;
        var generic = t.IndexOf('<');
        if (generic > 0)
        {
            t = t.Substring(0, generic);
        }

        foreach (var suffix in TypeSuffixes)
        {
            if (t.Length > suffix.Length && t.EndsWith(suffix, StringComparison.Ordinal))
            {
                return t.Substring(0, t.Length - suffix.Length);
            }
        }

        return t;
    }

    private static string Humanize(string name)
    {
        var chars = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '_' || c == '-')
            {
                chars.Append(' ');
                continue;
            }

            if (i > 0 && char.IsUpper(c) && char.IsLower(name[i - 1]))
            {
                chars.Append(' ');
            }

            chars.Append(char.ToLowerInvariant(c));
        }

        return chars.ToString();
    }
}
