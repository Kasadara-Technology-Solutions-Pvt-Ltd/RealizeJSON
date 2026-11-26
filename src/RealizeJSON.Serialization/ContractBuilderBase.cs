using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using RealizeJSON.CodeAnalysis;
using RealizeJSON.Core;
using RealizeJSON.Core.Contracts;
using RealizeJSON.Core.Json;
using RealizeJSON.Diagnostics;

namespace RealizeJSON.Serialization;

/// <summary>Enum representation requested by a converter or the profile.</summary>
/// <param name="AsStrings">Write names instead of numbers.</param>
/// <param name="Naming">Naming policy applied to member names.</param>
/// <param name="Source">Provenance description.</param>
public sealed record EnumStyle(bool AsStrings, NamingPolicyKind Naming, string Source)
{
    public string Key => AsStrings ? "s:" + Naming.ToString() : "n";
}

/// <summary>Result of recognizing a converter attribute.</summary>
public sealed record ConverterDecision(string DisplayName, bool IsKnown, EnumStyle? EnumStyle = null, bool AppliesToItems = false)
{
    /// <summary>For known converters that replace the representation with a scalar (e.g. Unix time).</summary>
    public PrimitiveKind? ReplacementPrimitive { get; init; }

    /// <summary>Known converter that produces non-JSON output.</summary>
    public string? InvalidOutputReason { get; init; }
}

/// <summary>A member candidate discovered on a type hierarchy.</summary>
public sealed class MemberCandidate
{
    public MemberCandidate(ISymbol symbol, ITypeSymbol type, INamedTypeSymbol declaringType, string? hiddenBy)
    {
        Symbol = symbol;
        Type = type;
        DeclaringType = declaringType;
        HiddenBy = hiddenBy;
    }

    public ISymbol Symbol { get; }

    public ITypeSymbol Type { get; }

    public INamedTypeSymbol DeclaringType { get; }

    /// <summary>Declaring type of a more-derived member with the same name, if this member is hidden.</summary>
    public string? HiddenBy { get; }

    public IPropertySymbol? Property => Symbol as IPropertySymbol;

    public IFieldSymbol? Field => Symbol as IFieldSymbol;
}

/// <summary>
/// Shared contract-building pipeline. One instance per analysis; not thread-safe. Serializer-specific rules are
/// supplied by derived classes. No user code is executed: only compiler symbols are inspected.
/// </summary>
public abstract class ContractBuilderBase
{
    private const int MaxContracts = 4000;
    private readonly Dictionary<CacheKey, JsonContract> _cache = new();
    private readonly List<PreviewDiagnostic> _diagnostics = new();
    private readonly HashSet<string> _diagnosticKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);

    protected ContractBuilderBase(Compilation compilation, SerializationProfile profile, CancellationToken cancellationToken)
    {
        Compilation = compilation;
        Profile = profile;
        CancellationToken = cancellationToken;
    }

    protected Compilation Compilation { get; }

    protected SerializationProfile Profile { get; }

    protected CancellationToken CancellationToken { get; }

    protected abstract SerializerKind Serializer { get; }

    protected string SerializerName => Serializer == SerializerKind.SystemTextJson ? "System.Text.Json" : "Newtonsoft.Json";

    public ResolvedContract Build(ITypeSymbol root, IJsonValueFormatter formatter)
    {
        var contract = Resolve(root, null);
        var result = new ResolvedContract(contract, Profile, formatter);
        foreach (var d in _diagnostics)
        {
            result.Diagnostics.Add(d);
        }

        foreach (var f in _files)
        {
            result.ContributingFiles.Add(f);
        }

        return result;
    }

    /// <summary>Resolves the contract for a type (Nullable&lt;T&gt; is unwrapped).</summary>
    public JsonContract Resolve(ITypeSymbol type, EnumStyle? enumStyle)
    {
        CancellationToken.ThrowIfCancellationRequested();
        if (type.TryGetNullableUnderlying(out var underlying))
        {
            type = underlying;
        }

        var key = new CacheKey(type, enumStyle?.Key ?? string.Empty);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (_cache.Count >= MaxContracts)
        {
            return new UnsupportedContract(type.ToIdentity(), "type graph too large to analyze");
        }

        var contract = Create(type, enumStyle, key);
        _cache[key] = contract;
        return contract;
    }

    protected void Report(string id, string? jsonPath, string? memberPath, params object[] args)
    {
        var diagnostic = DiagnosticCatalog.Create(id, jsonPath, memberPath, args);
        if (_diagnosticKeys.Add(diagnostic.Id + "|" + diagnostic.Message + "|" + memberPath))
        {
            _diagnostics.Add(diagnostic);
        }
    }

    protected string ApplyNaming(NamingPolicyKind policy, string name) => Core.Naming.NamingPolicies.Convert(policy, name, Serializer);

    // ---- Serializer hooks ----------------------------------------------------------------------------------

    /// <summary>Recognizes a type-level converter attribute, or returns null.</summary>
    protected abstract ConverterDecision? GetTypeConverter(INamedTypeSymbol type);

    /// <summary>Returns a reason when a primitive is not supported by the serializer, otherwise null.</summary>
    protected abstract string? GetUnsupportedPrimitiveReason(PrimitiveKind kind);

    /// <summary>Wire name of an enum member when written as a string.</summary>
    protected abstract string GetEnumMemberWireName(IFieldSymbol field, EnumStyle style);

    /// <summary>Name of an enum member used as a dictionary key; null means "same as the wire name".</summary>
    protected virtual string? GetEnumMemberKeyName(IFieldSymbol field) => null;

    /// <summary>Contract for multi-dimensional arrays.</summary>
    protected abstract JsonContract CreateMultiDimensionalArray(IArrayTypeSymbol array, TypeIdentity identity);

    /// <summary>True when the key type is supported; sets the key naming policy applied at write time.</summary>
    protected abstract bool IsSupportedDictionaryKey(JsonContract key, out NamingPolicyKind keyNaming);

    /// <summary>Populates members, polymorphism and type metadata.</summary>
    protected abstract void PopulateObject(ObjectContract contract, INamedTypeSymbol type);

    /// <summary>Default enum style from the profile.</summary>
    protected virtual EnumStyle DefaultEnumStyle => Profile.Enums == EnumRepresentation.Strings
        ? new EnumStyle(true, Profile.EnumNaming, "Global string enum converter")
        : new EnumStyle(false, NamingPolicyKind.Unchanged, "Default enum handling (numbers)");

    // ---- Pipeline ------------------------------------------------------------------------------------------

    private JsonContract Create(ITypeSymbol type, EnumStyle? enumStyle, CacheKey key)
    {
        var identity = type.ToIdentity();
        switch (type)
        {
            case ITypeParameterSymbol parameter:
                Report(DiagnosticIds.OpenGenericParameter, null, null, parameter.Name);
                return new AnyContract(identity, AnyKind.Unbound);
            case IErrorTypeSymbol:
                Report(DiagnosticIds.UnresolvedSymbol, null, null, type.ToShortDisplay());
                return new AnyContract(identity, AnyKind.Unbound);
            case IPointerTypeSymbol or IFunctionPointerTypeSymbol:
                return Unsupported(identity, "pointer types cannot be serialized");
            case IDynamicTypeSymbol:
                Report(DiagnosticIds.UnresolvedRuntimeType, null, null, "dynamic", "The value is shown as an empty object.");
                return new AnyContract(identity, AnyKind.Dynamic);
            case IArrayTypeSymbol array:
                if (array.Rank == 1 && array.ElementType.SpecialType == SpecialType.System_Byte)
                {
                    return new PrimitiveContract(identity, PrimitiveKind.ByteArray);
                }

                if (array.Rank > 1)
                {
                    return CreateMultiDimensionalArray(array, identity);
                }

                return CreateCollection(identity, array.ElementType, enumStyle, requiresUnique: false);
            case INamedTypeSymbol named:
                return CreateNamed(named, identity, enumStyle, key);
            default:
                return Unsupported(identity, "type kind is not supported");
        }
    }

    protected JsonContract CreateCollection(TypeIdentity identity, ITypeSymbol elementType, EnumStyle? itemEnumStyle, bool requiresUnique, int rank = 1)
    {
        var element = Resolve(elementType, itemEnumStyle);
        var contract = new CollectionContract(identity, element, rank, requiresUnique)
        {
            ElementIsNullable = !elementType.IsValueType || elementType.TryGetNullableUnderlying(out _),
        };
        if (element.Classification >= Classification.Unsupported)
        {
            contract.Downgrade(Classification.Estimated);
        }

        return contract;
    }

    private JsonContract CreateNamed(INamedTypeSymbol named, TypeIdentity identity, EnumStyle? enumStyle, CacheKey key)
    {
        if (named.TypeKind == TypeKind.Error)
        {
            Report(DiagnosticIds.UnresolvedSymbol, null, null, named.ToShortDisplay());
            return new AnyContract(identity, AnyKind.Unbound);
        }

        if (named.IsRefLikeType)
        {
            return Unsupported(identity, "ref struct types cannot be stored or serialized");
        }

        if (named.TypeKind == TypeKind.Delegate || InheritsFrom(named, KnownTypeNames.Delegate))
        {
            return Unsupported(identity, "delegates cannot be serialized");
        }

        if (named.SpecialType == SpecialType.System_Object)
        {
            Report(DiagnosticIds.UnresolvedRuntimeType, null, null, "object", "The value is shown as an empty object.");
            return new AnyContract(identity, AnyKind.Object);
        }

        if (named.SpecialType is SpecialType.System_IntPtr or SpecialType.System_UIntPtr ||
            InheritsFrom(named, KnownTypeNames.Type) || InheritsFrom(named, KnownTypeNames.MemberInfo) ||
            InheritsFrom(named, KnownTypeNames.Task) || named.OriginalDefinition.Is(KnownTypeNames.AsyncEnumerable))
        {
            return Unsupported(identity, "the serializer does not support this framework type");
        }

        var converter = GetTypeConverter(named);
        if (converter is { IsKnown: false })
        {
            Report(DiagnosticIds.UnknownConverter, null, named.ToShortDisplay(), converter.DisplayName);
        }

        if (converter?.EnumStyle is not null && named.TypeKind == TypeKind.Enum)
        {
            enumStyle ??= converter.EnumStyle;
        }

        JsonContract contract;
        if (converter?.ReplacementPrimitive is { } replacement)
        {
            contract = new PrimitiveContract(identity, replacement);
        }
        else if (TryGetPrimitive(named, out var primitive))
        {
            var reason = GetUnsupportedPrimitiveReason(primitive);
            contract = reason is null ? new PrimitiveContract(identity, primitive) : Unsupported(identity, reason);
        }
        else if (IsJsonDocumentType(named))
        {
            contract = new AnyContract(identity, AnyKind.JsonDocument, Classification.Estimated);
            Report(DiagnosticIds.UnresolvedRuntimeType, null, null, named.ToShortDisplay(), "Arbitrary JSON content is shown as an empty object.");
        }
        else if (named.TypeKind == TypeKind.Enum)
        {
            contract = CreateEnum(named, identity, enumStyle ?? DefaultEnumStyle);
        }
        else if (TryGetDictionary(named, out var keyType, out var valueType))
        {
            contract = CreateDictionary(identity, keyType, valueType);
        }
        else if (named.SpecialType != SpecialType.System_String && TryGetEnumerable(named, out var elementType, out var unique))
        {
            contract = CreateCollection(identity, elementType, converter?.AppliesToItems == true ? converter.EnumStyle : null, unique);
        }
        else
        {
            var obj = new ObjectContract(identity)
            {
                IsAbstract = named.TypeKind == TypeKind.Interface || named.IsAbstract,
                Location = named.GetSourceLocation(),
            };
            _cache[key] = obj;
            foreach (var reference in named.DeclaringSyntaxReferences)
            {
                if (!string.IsNullOrEmpty(reference.SyntaxTree.FilePath))
                {
                    _files.Add(reference.SyntaxTree.FilePath);
                }
            }

            PopulateObject(obj, named);
            contract = obj;
        }

        if (converter is not null)
        {
            contract.Converter = converter.DisplayName;
            contract.Notes.Add(converter.IsKnown
                ? $"Type-level converter {converter.DisplayName} is recognized."
                : $"Type-level converter {converter.DisplayName} is unknown; the default representation is shown.");
            if (!converter.IsKnown)
            {
                contract.Downgrade(Classification.Unsupported);
            }
        }

        return contract;
    }

    protected UnsupportedContract Unsupported(TypeIdentity identity, string reason)
    {
        Report(DiagnosticIds.UnsupportedType, null, null, identity.DisplayName, SerializerName, reason);
        return new UnsupportedContract(identity, reason);
    }

    private EnumContract CreateEnum(INamedTypeSymbol type, TypeIdentity identity, EnumStyle style)
    {
        var underlying = type.EnumUnderlyingType;
        var underlyingKind = underlying is not null && TryGetPrimitive(underlying, out var k) ? k : PrimitiveKind.Int32;
        var signed = underlyingKind is PrimitiveKind.SByte or PrimitiveKind.Int16 or PrimitiveKind.Int32 or PrimitiveKind.Int64;
        var members = new List<EnumMemberInfo>();
        foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
        {
            if (!field.HasConstantValue || field.ConstantValue is null)
            {
                continue;
            }

            var raw = ToRaw(field.ConstantValue);
            var numeric = signed
                ? unchecked((long)raw).ToString(CultureInfo.InvariantCulture)
                : raw.ToString(CultureInfo.InvariantCulture);
            members.Add(new EnumMemberInfo(field.Name, GetEnumMemberWireName(field, style), raw, numeric) { KeyName = GetEnumMemberKeyName(field) });
        }

        var contract = new EnumContract(identity, members, type.HasAttribute(KnownTypeNames.FlagsAttribute), style.AsStrings, underlyingKind);
        contract.Notes.Add(style.Source);
        return contract;
    }

    internal static ulong ToRaw(object value) => value switch
    {
        sbyte v => unchecked((ulong)v),
        short v => unchecked((ulong)v),
        int v => unchecked((ulong)v),
        long v => unchecked((ulong)v),
        byte v => v,
        ushort v => v,
        uint v => v,
        ulong v => v,
        _ => 0,
    };

    private JsonContract CreateDictionary(TypeIdentity identity, ITypeSymbol keyType, ITypeSymbol valueType)
    {
        var key = Resolve(keyType, null);
        var value = Resolve(valueType, null);
        if (!IsSupportedDictionaryKey(key, out var keyNaming))
        {
            Report(DiagnosticIds.UnsupportedDictionaryKey, null, null, keyType.ToShortDisplay(), SerializerName);
            return new DictionaryContract(identity, key, value, NamingPolicyKind.Unchanged, Classification.Unsupported);
        }

        return new DictionaryContract(identity, key, value, keyNaming);
    }

    private static bool InheritsFrom(INamedTypeSymbol type, string fullMetadataName)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (t.Is(fullMetadataName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsJsonDocumentType(INamedTypeSymbol type) =>
        type.Is(KnownTypeNames.JsonElement) || type.Is(KnownTypeNames.JsonDocument) ||
        InheritsFrom(type, KnownTypeNames.JsonNode) || InheritsFrom(type, KnownTypeNames.JToken) ||
        type.Is(KnownTypeNames.ExpandoObject);

    public static bool TryGetPrimitive(ITypeSymbol type, out PrimitiveKind kind)
    {
        kind = type.SpecialType switch
        {
            SpecialType.System_Boolean => PrimitiveKind.Boolean,
            SpecialType.System_String => PrimitiveKind.String,
            SpecialType.System_Char => PrimitiveKind.Char,
            SpecialType.System_SByte => PrimitiveKind.SByte,
            SpecialType.System_Byte => PrimitiveKind.Byte,
            SpecialType.System_Int16 => PrimitiveKind.Int16,
            SpecialType.System_UInt16 => PrimitiveKind.UInt16,
            SpecialType.System_Int32 => PrimitiveKind.Int32,
            SpecialType.System_UInt32 => PrimitiveKind.UInt32,
            SpecialType.System_Int64 => PrimitiveKind.Int64,
            SpecialType.System_UInt64 => PrimitiveKind.UInt64,
            SpecialType.System_Single => PrimitiveKind.Single,
            SpecialType.System_Double => PrimitiveKind.Double,
            SpecialType.System_Decimal => PrimitiveKind.Decimal,
            SpecialType.System_DateTime => PrimitiveKind.DateTime,
            _ => (PrimitiveKind)(-1),
        };

        if ((int)kind >= 0)
        {
            return true;
        }

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        var name = named.OriginalDefinition.GetFullMetadataName();
        switch (name)
        {
            case KnownTypeNames.DateTimeOffset: kind = PrimitiveKind.DateTimeOffset; return true;
            case KnownTypeNames.DateOnly: kind = PrimitiveKind.DateOnly; return true;
            case KnownTypeNames.TimeOnly: kind = PrimitiveKind.TimeOnly; return true;
            case KnownTypeNames.TimeSpan: kind = PrimitiveKind.TimeSpan; return true;
            case KnownTypeNames.Guid: kind = PrimitiveKind.Guid; return true;
            case KnownTypeNames.Uri: kind = PrimitiveKind.Uri; return true;
            case KnownTypeNames.Version: kind = PrimitiveKind.Version; return true;
            case KnownTypeNames.Half: kind = PrimitiveKind.Half; return true;
            case KnownTypeNames.Int128: kind = PrimitiveKind.Int128; return true;
            case KnownTypeNames.UInt128: kind = PrimitiveKind.UInt128; return true;
            case KnownTypeNames.BigInteger: kind = PrimitiveKind.BigInteger; return true;
            default: return false;
        }
    }

    private bool TryGetDictionary(INamedTypeSymbol type, out ITypeSymbol key, out ITypeSymbol value)
    {
        foreach (var candidate in SelfAndInterfaces(type))
        {
            if (candidate.Is(KnownTypeNames.IDictionary2) || candidate.Is(KnownTypeNames.IReadOnlyDictionary2))
            {
                key = candidate.TypeArguments[0];
                value = candidate.TypeArguments[1];
                return true;
            }
        }

        foreach (var candidate in SelfAndInterfaces(type))
        {
            if (candidate.Is(KnownTypeNames.IDictionary))
            {
                // Non-generic dictionaries: keys are converted with ToString at runtime.
                var stringType = Compilation.GetSpecialType(SpecialType.System_String);
                var objectType = Compilation.GetSpecialType(SpecialType.System_Object);
                key = stringType;
                value = objectType;
                return true;
            }
        }

        key = type;
        value = type;
        return false;
    }

    private bool TryGetEnumerable(INamedTypeSymbol type, out ITypeSymbol element, out bool unique)
    {
        unique = SelfAndInterfaces(type).Any(i => i.Is(KnownTypeNames.ISet1) || i.Is(KnownTypeNames.IReadOnlySet1));
        foreach (var candidate in SelfAndInterfaces(type))
        {
            if (candidate.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            {
                element = candidate.TypeArguments[0];
                return true;
            }
        }

        foreach (var candidate in SelfAndInterfaces(type))
        {
            if (candidate.SpecialType == SpecialType.System_Collections_IEnumerable)
            {
                element = Compilation.GetSpecialType(SpecialType.System_Object);
                return true;
            }
        }

        element = type;
        return false;
    }

    private static IEnumerable<INamedTypeSymbol> SelfAndInterfaces(INamedTypeSymbol type)
    {
        yield return type;
        foreach (var i in type.AllInterfaces)
        {
            yield return i;
        }
    }

    /// <summary>
    /// Collects instance properties and fields on the type hierarchy, most-derived type first (matching both
    /// serializers' reflection order). Overridden members are represented once; hidden members are flagged.
    /// </summary>
    protected IReadOnlyList<MemberCandidate> CollectMembers(INamedTypeSymbol type)
    {
        var result = new List<MemberCandidate>();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var overridden = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        IEnumerable<INamedTypeSymbol> hierarchy = type.TypeKind == TypeKind.Interface
            ? new[] { type }.Concat(type.AllInterfaces)
            : type.SelfAndBaseTypes().Where(t => t.SpecialType is not (SpecialType.System_Object or SpecialType.System_ValueType));

        foreach (var declaring in hierarchy)
        {
            var declaredHere = new List<string>();
            IEnumerable<ISymbol> members = declaring.IsTupleType
                ? declaring.TupleElements.Select(e => (ISymbol)(e.CorrespondingTupleField ?? e))
                : declaring.GetMembers();
            foreach (var member in members)
            {
                CancellationToken.ThrowIfCancellationRequested();
                if (member.IsStatic || (member.IsImplicitlyDeclared && member is not IPropertySymbol && !declaring.IsTupleType))
                {
                    continue;
                }

                ITypeSymbol memberType;
                switch (member)
                {
                    case IPropertySymbol property:
                        if (property.IsIndexer || !property.ExplicitInterfaceImplementations.IsEmpty)
                        {
                            continue;
                        }

                        if (overridden.Contains(property.OriginalDefinition))
                        {
                            continue;
                        }

                        for (var o = property.OverriddenProperty; o is not null; o = o.OverriddenProperty)
                        {
                            overridden.Add(o.OriginalDefinition);
                        }

                        memberType = property.Type;
                        break;
                    case IFieldSymbol field:
                        if (field.IsConst || field.AssociatedSymbol is not null)
                        {
                            continue;
                        }

                        memberType = field.Type;
                        break;
                    default:
                        continue;
                }

                names.TryGetValue(member.Name, out var hiddenBy);
                result.Add(new MemberCandidate(member, memberType, declaring, hiddenBy));
                declaredHere.Add(member.Name);
            }

            foreach (var name in declaredHere)
            {
                if (!names.ContainsKey(name))
                {
                    names[name] = declaring.ToShortDisplay();
                }
            }
        }

        return result;
    }

    /// <summary>Creates a member contract shell with common metadata.</summary>
    protected static PropertyContract CreateMember(MemberCandidate candidate)
    {
        var symbol = candidate.Symbol;
        var type = candidate.Type;
        var member = new PropertyContract(
            symbol.Name,
            symbol is IFieldSymbol ? MemberKind.Field : MemberKind.Property,
            candidate.DeclaringType.ToFullDisplay(),
            type.ToShortDisplay())
        {
            Location = symbol.GetSourceLocation(),
            IsNullableValueType = type.TryGetNullableUnderlying(out _),
            IsNullableAnnotated = !type.IsValueType && type.NullableAnnotation == NullableAnnotation.Annotated,
            CanBeNull = !type.IsValueType || type.TryGetNullableUnderlying(out _),
        };

        if (symbol is IPropertySymbol property && IsComputed(property))
        {
            member.IsComputed = true;
            member.Downgrade(Classification.Estimated);
            member.Explanations.Add("The value is computed by a getter at runtime; the sample value is illustrative.");
        }

        if (symbol is IPropertySymbol { IsRequired: true } || symbol is IFieldSymbol { IsRequired: true })
        {
            member.IsRequired = true;
            member.Provenance.Add("C# required modifier (deserialization metadata)");
        }

        return member;
    }

    /// <summary>True when a source property has a getter body (not an auto-property).</summary>
    private static bool IsComputed(IPropertySymbol property)
    {
        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax declaration)
            {
                if (declaration.ExpressionBody is not null)
                {
                    return true;
                }

                return declaration.AccessorList?.Accessors.Any(a =>
                    a.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.GetAccessorDeclaration) && (a.Body is not null || a.ExpressionBody is not null)) == true;
            }
        }

        return false;
    }

    /// <summary>
    /// Within each declaring type, places properties before fields (System.Text.Json reflection order).
    /// Hierarchy order (most derived first) is preserved.
    /// </summary>
    protected static void PropertiesBeforeFields(ObjectContract contract)
    {
        var ordered = contract.Members
            .Select((m, i) => (m, i))
            .GroupBy(x => x.m.DeclaringType)
            .OrderBy(g => g.Min(x => x.i))
            .SelectMany(g => g.OrderBy(x => x.m.MemberKind == MemberKind.Field ? 1 : 0).ThenBy(x => x.i))
            .Select(x => x.m)
            .ToList();
        contract.Members.Clear();
        foreach (var m in ordered)
        {
            contract.Members.Add(m);
        }
    }

    /// <summary>
    /// Places all fields before all properties across the hierarchy (Newtonsoft.Json
    /// ReflectionUtils.GetFieldsAndProperties order).
    /// </summary>
    protected static void FieldsBeforeProperties(ObjectContract contract)
    {
        var ordered = contract.Members.Where(m => m.MemberKind == MemberKind.Field)
            .Concat(contract.Members.Where(m => m.MemberKind == MemberKind.Property))
            .ToList();
        contract.Members.Clear();
        foreach (var m in ordered)
        {
            contract.Members.Add(m);
        }
    }

    /// <summary>Stable sort of members by an order key.</summary>
    protected static void SortMembers(ObjectContract contract, Func<PropertyContract, int> orderKey)
    {
        var sorted = contract.Members.Select((m, i) => (m, i)).OrderBy(x => orderKey(x.m)).ThenBy(x => x.i).Select(x => x.m).ToList();
        contract.Members.Clear();
        foreach (var m in sorted)
        {
            contract.Members.Add(m);
        }
    }

    /// <summary>Flags duplicate JSON names among included members (the serializer throws at runtime).</summary>
    protected void CheckDuplicateNames(ObjectContract contract, string? reservedName = null)
    {
        var seen = new Dictionary<string, PropertyContract>(StringComparer.Ordinal);
        foreach (var member in contract.Members)
        {
            if (!member.Included || member.IsExtensionData)
            {
                continue;
            }

            if (reservedName is not null && member.JsonName == reservedName)
            {
                Report(DiagnosticIds.DiscriminatorConflict, null, member.ClrName, contract.Type.DisplayName, $"member '{member.ClrName}' uses the discriminator name '{reservedName}'");
                member.Downgrade(Classification.Unsupported);
            }

            if (seen.TryGetValue(member.JsonName, out var first))
            {
                Report(DiagnosticIds.DuplicateJsonName, null, member.ClrName, member.JsonName, $"{first.ClrName}, {member.ClrName}");
                member.Exclude($"Duplicate JSON name '{member.JsonName}' (also produced by {first.ClrName}); the serializer throws at runtime.");
                member.Downgrade(Classification.Unsupported);
                first.Downgrade(Classification.Unsupported);
                contract.Downgrade(Classification.Unsupported);
            }
            else
            {
                seen.Add(member.JsonName, member);
            }
        }
    }

    /// <summary>True when a public parameterless instance method <c>ShouldSerialize{Name}</c> exists.</summary>
    protected static bool HasMethodNamed(INamedTypeSymbol type, string name) =>
        type.SelfAndBaseTypes().Any(t => t.GetMembers(name).OfType<IMethodSymbol>().Any(m => !m.IsStatic && m.Parameters.IsEmpty));

    private readonly struct CacheKey : IEquatable<CacheKey>
    {
        private readonly ITypeSymbol _type;
        private readonly string _variant;

        public CacheKey(ITypeSymbol type, string variant)
        {
            _type = type;
            _variant = variant;
        }

        public bool Equals(CacheKey other) => SymbolEqualityComparer.Default.Equals(_type, other._type) && _variant == other._variant;

        public override bool Equals(object? obj) => obj is CacheKey other && Equals(other);

        public override int GetHashCode() => unchecked((SymbolEqualityComparer.Default.GetHashCode(_type) * 397) ^ StringComparer.Ordinal.GetHashCode(_variant));
    }
}
