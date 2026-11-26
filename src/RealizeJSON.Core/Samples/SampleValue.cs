using System;
using System.Collections.Generic;
using RealizeJSON.Core.Contracts;

namespace RealizeJSON.Core.Samples;

/// <summary>
/// Modeled CLR value used to generate the preview. Omission rules are evaluated against these values,
/// not against their JSON text.
/// </summary>
public abstract class SampleValue
{
    /// <summary>True when the value equals the CLR default of its type.</summary>
    public abstract bool IsClrDefault { get; }
}

public sealed class NullSampleValue : SampleValue
{
    public static NullSampleValue Instance { get; } = new();

    private NullSampleValue()
    {
    }

    public override bool IsClrDefault => true;
}

/// <summary>A scalar CLR value (see <see cref="PrimitiveKind"/> for the boxed representation).</summary>
public sealed class ScalarSampleValue : SampleValue
{
    public ScalarSampleValue(PrimitiveKind kind, object value, bool isDefault)
    {
        Kind = kind;
        Value = value;
        IsDefault = isDefault;
    }

    public PrimitiveKind Kind { get; }

    /// <summary>
    /// Boxed value: bool, string, char, long (signed integrals), ulong (unsigned integrals), float (Single), double (Half/Double),
    /// decimal, DateTime, DateTimeOffset, <see cref="DateOnlyValue"/>, <see cref="TimeOnlyValue"/>, TimeSpan, Guid,
    /// string (Uri, Version, BigInteger, Int128), byte[].
    /// </summary>
    public object Value { get; }

    private bool IsDefault { get; }

    public override bool IsClrDefault => IsDefault;
}

/// <summary>Calendar date (netstandard2.0 has no DateOnly).</summary>
public readonly record struct DateOnlyValue(int Year, int Month, int Day);

/// <summary>Time of day in ticks (netstandard2.0 has no TimeOnly).</summary>
public readonly record struct TimeOnlyValue(long Ticks);

public sealed class EnumSampleValue : SampleValue
{
    public EnumSampleValue(EnumContract contract, ulong rawValue)
    {
        Contract = contract;
        RawValue = rawValue;
    }

    public EnumContract Contract { get; }

    public ulong RawValue { get; }

    public override bool IsClrDefault => RawValue == 0;
}

/// <summary>Object value with member values in contract order.</summary>
public sealed class ObjectSampleValue : SampleValue
{
    public ObjectSampleValue(ObjectContract contract, bool isValueType)
    {
        Contract = contract;
        IsValueType = isValueType;
    }

    /// <summary>Runtime contract (a derived variant for polymorphic previews).</summary>
    public ObjectContract Contract { get; }

    public bool IsValueType { get; }

    public PolymorphicVariant? Variant { get; set; }

    /// <summary>Reference id assigned when reference preservation is modeled.</summary>
    public int ReferenceId { get; set; }

    public IList<KeyValuePair<PropertyContract, SampleValue>> Members { get; } = new List<KeyValuePair<PropertyContract, SampleValue>>();

    /// <summary>True when members were not generated because a limit was reached.</summary>
    public bool Truncated { get; set; }

    public override bool IsClrDefault
    {
        get
        {
            if (!IsValueType)
            {
                return false;
            }

            foreach (var member in Members)
            {
                if (!member.Value.IsClrDefault)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

public sealed class CollectionSampleValue : SampleValue
{
    public CollectionSampleValue(CollectionContract contract) => Contract = contract;

    public CollectionContract Contract { get; }

    public IList<SampleValue> Items { get; } = new List<SampleValue>();

    /// <summary>For multi-dimensional arrays: length of each dimension.</summary>
    public IReadOnlyList<int>? Dimensions { get; set; }

    public bool Truncated { get; set; }

    public override bool IsClrDefault => false;
}

public sealed class DictionarySampleValue : SampleValue
{
    public DictionarySampleValue(DictionaryContract contract) => Contract = contract;

    public DictionaryContract Contract { get; }

    public IList<KeyValuePair<SampleValue, SampleValue>> Entries { get; } = new List<KeyValuePair<SampleValue, SampleValue>>();

    public override bool IsClrDefault => false;
}

/// <summary>Opaque runtime value (System.Object instance, JsonElement…) rendered as an empty object.</summary>
public sealed class AnySampleValue : SampleValue
{
    public AnySampleValue(AnyContract contract) => Contract = contract;

    public AnyContract Contract { get; }

    public override bool IsClrDefault => false;
}
