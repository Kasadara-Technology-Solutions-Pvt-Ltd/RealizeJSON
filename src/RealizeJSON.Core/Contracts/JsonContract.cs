using System;
using System.Collections.Generic;
using RealizeJSON.Core.Json;

namespace RealizeJSON.Core.Contracts;

/// <summary>Kind of resolved JSON contract.</summary>
public enum ContractKind
{
    Primitive,
    Enum,
    Collection,
    Dictionary,
    Object,

    /// <summary>Arbitrary JSON whose shape is only known at runtime (object, dynamic, JsonElement, JToken…).</summary>
    Any,

    Unsupported,
}

/// <summary>Primitive and special scalar types with serializer-defined representations.</summary>
public enum PrimitiveKind
{
    Boolean,
    String,
    Char,
    SByte,
    Byte,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Int128,
    UInt128,
    Half,
    Single,
    Double,
    Decimal,
    DateTime,
    DateTimeOffset,
    DateOnly,
    TimeOnly,
    TimeSpan,
    Guid,
    Uri,
    ByteArray,
    Version,
    BigInteger,
}

/// <summary>Base class of the resolved serialization contract graph. The graph may contain cycles.</summary>
public abstract class JsonContract
{
    protected JsonContract(TypeIdentity type, Classification classification)
    {
        Type = type;
        Classification = classification;
    }

    public abstract ContractKind Kind { get; }

    public TypeIdentity Type { get; }

    public Classification Classification { get; internal set; }

    /// <summary>Explanations attached to the type (converter decisions, polymorphism notes…).</summary>
    public IList<string> Notes { get; } = new List<string>();

    /// <summary>Display name of a type-level converter, if any.</summary>
    public string? Converter { get; set; }

    public void Downgrade(Classification classification)
    {
        if (classification > Classification)
        {
            Classification = classification;
        }
    }
}

public sealed class PrimitiveContract : JsonContract
{
    public PrimitiveContract(TypeIdentity type, PrimitiveKind primitive, Classification classification = Classification.Resolved)
        : base(type, classification) => Primitive = primitive;

    public override ContractKind Kind => ContractKind.Primitive;

    public PrimitiveKind Primitive { get; }

    public bool IsNumeric => Primitive is >= PrimitiveKind.SByte and <= PrimitiveKind.Decimal or PrimitiveKind.BigInteger;
}

/// <summary>A declared enum member.</summary>
/// <param name="ClrName">Declared name.</param>
/// <param name="WireName">Name written when the enum is serialized as a string.</param>
/// <param name="RawValue">Underlying value as unsigned bits.</param>
/// <param name="NumericText">Underlying value as JSON number text.</param>
public sealed record EnumMemberInfo(string ClrName, string WireName, ulong RawValue, string NumericText)
{
    /// <summary>Name used when the enum is a dictionary key (serializer-specific); defaults to the wire name.</summary>
    public string? KeyName { get; init; }
}

public sealed class EnumContract : JsonContract
{
    public EnumContract(TypeIdentity type, IReadOnlyList<EnumMemberInfo> members, bool isFlags, bool asStrings, PrimitiveKind underlying, Classification classification = Classification.Resolved)
        : base(type, classification)
    {
        Members = members;
        IsFlags = isFlags;
        AsStrings = asStrings;
        Underlying = underlying;
    }

    public override ContractKind Kind => ContractKind.Enum;

    public IReadOnlyList<EnumMemberInfo> Members { get; }

    public bool IsFlags { get; }

    public bool AsStrings { get; }

    public PrimitiveKind Underlying { get; }

    /// <summary>True when the underlying type is signed.</summary>
    public bool IsSigned => Underlying is PrimitiveKind.SByte or PrimitiveKind.Int16 or PrimitiveKind.Int32 or PrimitiveKind.Int64;
}

public sealed class CollectionContract : JsonContract
{
    public CollectionContract(TypeIdentity type, JsonContract element, int rank, bool requiresUniqueElements, Classification classification = Classification.Resolved)
        : base(type, classification)
    {
        Element = element;
        Rank = rank;
        RequiresUniqueElements = requiresUniqueElements;
    }

    public override ContractKind Kind => ContractKind.Collection;

    public JsonContract Element { get; }

    /// <summary>Array rank; values above 1 are written as nested arrays where supported.</summary>
    public int Rank { get; }

    /// <summary>True for sets; samples must be unique.</summary>
    public bool RequiresUniqueElements { get; }

    /// <summary>True when the element is nullable (reference type or Nullable&lt;T&gt;).</summary>
    public bool ElementIsNullable { get; set; }
}

public sealed class DictionaryContract : JsonContract
{
    public DictionaryContract(TypeIdentity type, JsonContract key, JsonContract value, NamingPolicyKind keyNaming, Classification classification = Classification.Resolved)
        : base(type, classification)
    {
        Key = key;
        Value = value;
        KeyNaming = keyNaming;
    }

    public override ContractKind Kind => ContractKind.Dictionary;

    public JsonContract Key { get; }

    public JsonContract Value { get; }

    /// <summary>Naming policy applied to keys at write time.</summary>
    public NamingPolicyKind KeyNaming { get; }
}

/// <summary>Why a value shape cannot be modeled statically.</summary>
public enum AnyKind
{
    Object,
    Dynamic,
    JsonDocument,
    AbstractOrInterface,

    /// <summary>Unbound generic type parameter or unresolved symbol; rendered as null.</summary>
    Unbound,
}

public sealed class AnyContract : JsonContract
{
    public AnyContract(TypeIdentity type, AnyKind anyKind, Classification classification = Classification.Unresolved)
        : base(type, classification) => AnyKind = anyKind;

    public override ContractKind Kind => ContractKind.Any;

    public AnyKind AnyKind { get; }
}

public sealed class UnsupportedContract : JsonContract
{
    public UnsupportedContract(TypeIdentity type, string reason)
        : base(type, Classification.Unsupported) => Reason = reason;

    public override ContractKind Kind => ContractKind.Unsupported;

    public string Reason { get; }
}

/// <summary>A configured polymorphic variant.</summary>
/// <param name="Type">Derived type identity.</param>
/// <param name="VariantKey">Stable key used for selection (fully qualified type name).</param>
/// <param name="DiscriminatorJson">Discriminator literal as JSON text (e.g. <c>"dog"</c> or <c>1</c>), or null when none is written.</param>
public sealed record PolymorphicVariant(TypeIdentity Type, string VariantKey, string? DiscriminatorJson)
{
    /// <summary>Resolved contract of the derived type (assigned after construction to allow cycles).</summary>
    public ObjectContract? Contract { get; set; }
}

public sealed class PolymorphismInfo
{
    public PolymorphismInfo(string discriminatorPropertyName, string source)
    {
        DiscriminatorPropertyName = discriminatorPropertyName;
        Source = source;
    }

    public string DiscriminatorPropertyName { get; }

    /// <summary>Attribute or setting that configured polymorphism.</summary>
    public string Source { get; }

    public IList<PolymorphicVariant> Variants { get; } = new List<PolymorphicVariant>();

    /// <summary>Discriminator for the declared (base) type itself, when the base is registered as a derived type.</summary>
    public string? BaseDiscriminatorJson { get; set; }
}

public sealed class ObjectContract : JsonContract
{
    public ObjectContract(TypeIdentity type, Classification classification = Classification.Resolved)
        : base(type, classification)
    {
    }

    public override ContractKind Kind => ContractKind.Object;

    /// <summary>All analyzed members in serialization order; excluded members are retained for the inspector.</summary>
    public IList<PropertyContract> Members { get; } = new List<PropertyContract>();

    public PolymorphismInfo? Polymorphism { get; set; }

    /// <summary>True when the declared type is abstract or an interface.</summary>
    public bool IsAbstract { get; set; }

    /// <summary>Deserialization constructor recorded as metadata (never invoked).</summary>
    public string? ConstructorMetadata { get; set; }

    public SourceLocation? Location { get; set; }

    public IEnumerable<PropertyContract> IncludedMembers
    {
        get
        {
            foreach (var member in Members)
            {
                if (member.Included && !member.IsExtensionData)
                {
                    yield return member;
                }
            }
        }
    }
}

/// <summary>Write-time omission rule for a member.</summary>
public enum MemberIgnoreCondition
{
    Never,
    WhenWritingNull,
    WhenWritingDefault,

    /// <summary>Inclusion depends on runtime code (ShouldSerialize, *Specified…).</summary>
    Conditional,
}

public enum MemberKind
{
    Property,
    Field,
}

/// <summary>Resolved serialization decision for one CLR member.</summary>
public sealed class PropertyContract
{
    public PropertyContract(string clrName, MemberKind memberKind, string declaringType, string clrTypeDisplay)
    {
        ClrName = clrName;
        MemberKind = memberKind;
        DeclaringType = declaringType;
        ClrTypeDisplay = clrTypeDisplay;
        JsonName = clrName;
    }

    public string ClrName { get; }

    public MemberKind MemberKind { get; }

    /// <summary>Fully qualified declaring type (used to disambiguate hidden members).</summary>
    public string DeclaringType { get; }

    public string ClrTypeDisplay { get; }

    public string JsonName { get; set; }

    /// <summary>Resolved value contract; null for members that were excluded before type resolution.</summary>
    public JsonContract? Contract { get; set; }

    public bool Included { get; set; } = true;

    public MemberIgnoreCondition IgnoreCondition { get; set; } = MemberIgnoreCondition.Never;

    /// <summary>Explicit order, when one was specified.</summary>
    public int? Order { get; set; }

    /// <summary>The member can hold null at runtime (reference type or Nullable&lt;T&gt;).</summary>
    public bool CanBeNull { get; set; }

    /// <summary>Reference type annotated nullable (<c>string?</c>); annotation only, not a runtime value.</summary>
    public bool IsNullableAnnotated { get; set; }

    public bool IsNullableValueType { get; set; }

    public bool IsReadOnly { get; set; }

    /// <summary>The value is computed by a getter body at runtime; the sample value is illustrative only.</summary>
    public bool IsComputed { get; set; }

    public bool IsRequired { get; set; }

    /// <summary>The serializer rejects a null value at write time (Newtonsoft Required.Always / DisallowNull).</summary>
    public bool RejectsNullOnWrite { get; set; }

    public bool IsExtensionData { get; set; }

    /// <summary>Numbers written as JSON strings.</summary>
    public bool NumberAsString { get; set; }

    /// <summary>Default value literal for default-omission semantics (Newtonsoft [DefaultValue]).</summary>
    public object? DefaultValue { get; set; }

    public bool HasDefaultValue { get; set; }

    /// <summary>Converter display name, if any.</summary>
    public string? Converter { get; set; }

    public Classification Classification { get; set; } = Classification.Resolved;

    /// <summary>Attribute/policy sources that determined the outcome.</summary>
    public IList<string> Provenance { get; } = new List<string>();

    /// <summary>Human-readable explanation lines.</summary>
    public IList<string> Explanations { get; } = new List<string>();

    public SourceLocation Location { get; set; } = SourceLocation.MetadataOnly;

    public void Exclude(string reason, string? provenance = null)
    {
        Included = false;
        Explanations.Add(reason);
        if (provenance is not null)
        {
            Provenance.Add(provenance);
        }
    }

    public void Downgrade(Classification classification)
    {
        if (classification > Classification)
        {
            Classification = classification;
        }
    }

    public override string ToString() => $"{DeclaringType}.{ClrName} -> {(Included ? JsonName : "(excluded)")}";
}

/// <summary>Result of contract resolution for a root type.</summary>
public sealed class ResolvedContract
{
    public ResolvedContract(JsonContract root, SerializationProfile profile, IJsonValueFormatter formatter)
    {
        Root = root;
        Profile = profile;
        Formatter = formatter;
    }

    public JsonContract Root { get; }

    public SerializationProfile Profile { get; }

    public IJsonValueFormatter Formatter { get; }

    public IList<PreviewDiagnostic> Diagnostics { get; } = new List<PreviewDiagnostic>();

    /// <summary>Source files that declare types in the contract graph (used for dependent-change refresh).</summary>
    public ISet<string> ContributingFiles { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
