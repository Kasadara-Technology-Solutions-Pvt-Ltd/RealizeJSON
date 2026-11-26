using System;
using System.Globalization;
using System.Linq;
using System.Text;
using RealizeJSON.Core.Contracts;
using RealizeJSON.Core.Samples;

namespace RealizeJSON.Serialization;

/// <summary>Shared ISO-8601 and enum formatting helpers matching the serializers' default writers.</summary>
public static class ScalarFormatting
{
    /// <summary>ISO 8601 date-time with trimmed fractional seconds (as written by both serializers by default).</summary>
    public static string FormatDateTime(DateTime value)
    {
        var sb = new StringBuilder(value.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss", CultureInfo.InvariantCulture));
        AppendFraction(sb, value.Ticks % TimeSpan.TicksPerSecond);
        if (value.Kind == DateTimeKind.Utc)
        {
            sb.Append('Z');
        }
        else if (value.Kind == DateTimeKind.Local)
        {
            sb.Append(value.ToString("zzz", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    public static string FormatDateTimeOffset(DateTimeOffset value)
    {
        var sb = new StringBuilder(value.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss", CultureInfo.InvariantCulture));
        AppendFraction(sb, value.Ticks % TimeSpan.TicksPerSecond);
        var offset = value.Offset;
        sb.Append(offset < TimeSpan.Zero ? '-' : '+');
        var abs = offset.Duration();
        sb.Append(abs.Hours.ToString("00", CultureInfo.InvariantCulture)).Append(':').Append(abs.Minutes.ToString("00", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    public static string FormatDateOnly(DateOnlyValue value) =>
        value.Year.ToString("0000", CultureInfo.InvariantCulture) + "-" + value.Month.ToString("00", CultureInfo.InvariantCulture) + "-" + value.Day.ToString("00", CultureInfo.InvariantCulture);

    public static string FormatTimeOnly(TimeOnlyValue value)
    {
        var t = TimeSpan.FromTicks(value.Ticks);
        var sb = new StringBuilder(t.Hours.ToString("00", CultureInfo.InvariantCulture) + ":" + t.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" + t.Seconds.ToString("00", CultureInfo.InvariantCulture));
        AppendFraction(sb, value.Ticks % TimeSpan.TicksPerSecond);
        return sb.ToString();
    }

    public static string FormatTimeSpan(TimeSpan value) => value.ToString("c", CultureInfo.InvariantCulture);

    private static void AppendFraction(StringBuilder sb, long fractionTicks)
    {
        if (fractionTicks == 0)
        {
            return;
        }

        sb.Append('.').Append(fractionTicks.ToString("0000000", CultureInfo.InvariantCulture).TrimEnd('0'));
    }

    /// <summary>Enum.ToString-compatible name formatting using wire names; null when not representable by names.</summary>
    public static string? FormatEnumName(EnumContract contract, ulong raw, bool keyNames = false)
    {
        foreach (var member in contract.Members)
        {
            if (member.RawValue == raw)
            {
                return keyNames ? member.KeyName ?? member.WireName : member.WireName;
            }
        }

        if (!contract.IsFlags || raw == 0)
        {
            return null;
        }

        var remaining = raw;
        var parts = contract.Members
            .Where(m => m.RawValue != 0)
            .GroupBy(m => m.RawValue)
            .Select(g => g.First())
            .OrderByDescending(m => m.RawValue)
            .Where(m =>
            {
                if ((remaining & m.RawValue) == m.RawValue)
                {
                    remaining &= ~m.RawValue;
                    return true;
                }

                return false;
            })
            .ToList();

        if (remaining != 0)
        {
            return null;
        }

        parts.Reverse();
        return string.Join(", ", parts.Select(p => keyNames ? p.KeyName ?? p.WireName : p.WireName));
    }
}
