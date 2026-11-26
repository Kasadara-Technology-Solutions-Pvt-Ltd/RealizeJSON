using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using RealizeJSON.CodeAnalysis;
using RealizeJSON.Core;
using RealizeJSON.Core.Contracts;
using RealizeJSON.Core.Json;
using RealizeJSON.Diagnostics;

namespace RealizeJSON.Serialization.SystemTextJson;

/// <summary>System.Text.Json rules (reflection-based serializer semantics, version 8.0+ by default).</summary>
public sealed class SystemTextJsonRuleProvider : IRuleProvider
{
    public SerializerKind Serializer => SerializerKind.SystemTextJson;

    public IJsonValueFormatter Formatter => SystemTextJsonFormatter.Instance;

    public ResolvedContract BuildContract(ITypeSymbol root, Compilation compilation, SerializationProfile profile, CancellationToken cancellationToken) =>
        new Builder(compilation, profile, cancellationToken).Build(root, Formatter);

    private sealed class Builder : ContractBuilderBase
    {
        private const string Ns = "System.Text.Json.Serialization.";
        private const string PropertyNameAttribute = Ns + "JsonPropertyNameAttribute";
        private const string IgnoreAttribute = Ns + "JsonIgnoreAttribute";
        private const string IncludeAttribute = Ns + "JsonIncludeAttribute";
        private const string OrderAttribute = Ns + "JsonPropertyOrderAttribute";
        private const string RequiredAttribute = Ns + "JsonRequiredAttribute";
        private const string ExtensionDataAttribute = Ns + "JsonExtensionDataAttribute";
        private const string NumberHandlingAttribute = Ns + "JsonNumberHandlingAttribute";
        private const string ConverterAttribute = Ns + "JsonConverterAttribute";
        private const string PolymorphicAttribute = Ns + "JsonPolymorphicAttribute";
        private const string DerivedTypeAttribute = Ns + "JsonDerivedTypeAttribute";
        private const string ConstructorAttribute = Ns + "JsonConstructorAttribute";
        private const string EnumMemberNameAttribute = Ns + "JsonStringEnumMemberNameAttribute";
        private const string StringEnumConverter = Ns + "JsonStringEnumConverter";
        private const string StringEnumConverterGeneric = Ns + "JsonStringEnumConverter`1";
        private const string NumberEnumConverterGeneric = Ns + "JsonNumberEnumConverter`1";
        private const string OnSerializing = Ns + "IJsonOnSerializing";
        private const string OnSerialized = Ns + "IJsonOnSerialized";

        // JsonIgnoreCondition values.
        private const int IgnoreNever = 0;
        private const int IgnoreAlways = 1;
        private const int IgnoreWhenWritingDefault = 2;
        private const int IgnoreWhenWritingNull = 3;
        private const int IgnoreWhenWriting = 4;
        private const int IgnoreWhenReading = 5;

        private const int NumberHandlingWriteAsString = 2;

        public Builder(Compilation compilation, SerializationProfile profile, CancellationToken cancellationToken)
            : base(compilation, profile, cancellationToken)
        {
        }

        protected override SerializerKind Serializer => SerializerKind.SystemTextJson;

        private int MajorVersion => int.TryParse(Profile.SerializerVersion.Split('.')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 8;

        protected override ConverterDecision? GetTypeConverter(INamedTypeSymbol type)
        {
            var attribute = type.FindAttribute(ConverterAttribute);
            return attribute is null ? null : Recognize(attribute, type);
        }

        private ConverterDecision Recognize(AttributeData attribute, ITypeSymbol targetType)
        {
            var converterType = attribute.GetConstructorArgument(0)?.Value as INamedTypeSymbol;
            if (converterType is null)
            {
                return new ConverterDecision(attribute.AttributeClass?.ToShortDisplay() ?? "JsonConverter", IsKnown: false);
            }

            var display = converterType.ToShortDisplay();
            var definition = converterType.OriginalDefinition.GetFullMetadataName();
            var isEnumTarget = Unwrap(targetType).TypeKind == TypeKind.Enum;
            if ((definition == StringEnumConverter || definition == StringEnumConverterGeneric) && isEnumTarget)
            {
                return new ConverterDecision(display, true, new EnumStyle(true, NamingPolicyKind.Unchanged, $"[JsonConverter(typeof({display}))]"));
            }

            if (definition == NumberEnumConverterGeneric && isEnumTarget)
            {
                return new ConverterDecision(display, true, new EnumStyle(false, NamingPolicyKind.Unchanged, $"[JsonConverter(typeof({display}))]"));
            }

            return new ConverterDecision(display, IsKnown: false);
        }

        private static ITypeSymbol Unwrap(ITypeSymbol type) => type.TryGetNullableUnderlying(out var u) ? u : type;

        protected override string? GetUnsupportedPrimitiveReason(PrimitiveKind kind) => kind switch
        {
            PrimitiveKind.BigInteger => "BigInteger has no built-in System.Text.Json converter",
            PrimitiveKind.Half or PrimitiveKind.Int128 or PrimitiveKind.UInt128 when MajorVersion < 8 => "requires System.Text.Json 8.0 or later",
            PrimitiveKind.DateOnly or PrimitiveKind.TimeOnly when MajorVersion < 7 => "requires System.Text.Json 7.0 or later",
            _ => null,
        };

        protected override string GetEnumMemberWireName(IFieldSymbol field, EnumStyle style)
        {
            var custom = field.FindAttribute(EnumMemberNameAttribute)?.GetConstructorArgument(0)?.Value as string;
            return custom ?? ApplyNaming(style.Naming, field.Name);
        }

        protected override JsonContract CreateMultiDimensionalArray(IArrayTypeSymbol array, TypeIdentity identity)
        {
            Report(DiagnosticIds.UnsupportedMultiDimensionalArray, null, null, identity.DisplayName);
            return new UnsupportedContract(identity, "multi-dimensional arrays are not supported");
        }

        protected override bool IsSupportedDictionaryKey(JsonContract key, out NamingPolicyKind keyNaming)
        {
            keyNaming = NamingPolicyKind.Unchanged;
            switch (key)
            {
                case EnumContract:
                case PrimitiveContract { Primitive: PrimitiveKind.String }:
                    keyNaming = Profile.DictionaryKeyNaming;
                    return true;
                case PrimitiveContract p:
                    return p.Primitive is not (PrimitiveKind.ByteArray or PrimitiveKind.BigInteger);
                case AnyContract { AnyKind: AnyKind.Object }:
                    // object keys are supported when the runtime key is a supported primitive.
                    return true;
                default:
                    return false;
            }
        }

        protected override void PopulateObject(ObjectContract contract, INamedTypeSymbol type)
        {
            if (contract.IsAbstract)
            {
                contract.Notes.Add("System.Text.Json serializes the members of the declared type; runtime-derived members are not written unless polymorphism is configured.");
            }

            if (type.IsTupleType && !Profile.IncludeFields)
            {
                Report(DiagnosticIds.ValueTupleFields, null, null, type.ToShortDisplay());
            }

            var callbacks = type.AllInterfaces.Where(i => i.Is(OnSerializing) || i.Is(OnSerialized)).Select(i => i.Name).ToList();
            if (callbacks.Count > 0)
            {
                Report(DiagnosticIds.SerializationCallbacks, null, null, type.ToShortDisplay(), string.Join(", ", callbacks));
                contract.Downgrade(Classification.Conditional);
            }

            var ctor = type.InstanceConstructors.FirstOrDefault(c => c.HasAttribute(ConstructorAttribute));
            if (ctor is not null)
            {
                contract.ConstructorMetadata = ctor.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            }

            var typeNumberHandling = type.FindAttribute(NumberHandlingAttribute);
            var hasExtensionData = false;

            foreach (var candidate in CollectMembers(type))
            {
                var member = CreateMember(candidate);
                contract.Members.Add(member);
                var symbol = candidate.Symbol;

                if (candidate.HiddenBy is not null)
                {
                    member.Exclude($"Hidden by a member with the same name in {candidate.HiddenBy}.");
                    continue;
                }

                var include = symbol.FindAttribute(IncludeAttribute) is not null;
                var isPublic = symbol.DeclaredAccessibility == Accessibility.Public;
                if (include)
                {
                    member.Provenance.Add("[JsonInclude]");
                }

                if (candidate.Property is { } property)
                {
                    var getterPublic = isPublic && property.GetMethod is { DeclaredAccessibility: Accessibility.Public };
                    member.IsReadOnly = property.SetMethod is null || property.SetMethod.DeclaredAccessibility != Accessibility.Public || !isPublic;
                    if (property.GetMethod is null)
                    {
                        member.Exclude("Write-only properties are not serialized.");
                        continue;
                    }

                    if (!getterPublic && !include)
                    {
                        member.Exclude(isPublic ? "The getter is not public." : "Non-public properties are not serialized without [JsonInclude].");
                        continue;
                    }

                    if (!getterPublic && include && MajorVersion < 8)
                    {
                        Report(DiagnosticIds.NonPublicJsonInclude, null, member.ClrName, member.ClrName);
                        member.Downgrade(Classification.Unsupported);
                    }
                }
                else if (candidate.Field is { } field)
                {
                    member.IsReadOnly = field.IsReadOnly;
                    if (!isPublic && !include)
                    {
                        member.Exclude("Non-public fields are not serialized.");
                        continue;
                    }

                    if (!include && !Profile.IncludeFields)
                    {
                        member.Exclude("Fields are not serialized unless IncludeFields is enabled or the field has [JsonInclude].");
                        continue;
                    }

                    if (!isPublic && MajorVersion < 8)
                    {
                        Report(DiagnosticIds.NonPublicJsonInclude, null, member.ClrName, member.ClrName);
                        member.Downgrade(Classification.Unsupported);
                    }

                    if (!include)
                    {
                        member.Provenance.Add("IncludeFields = true");
                    }
                }

                // [JsonIgnore]
                var ignore = symbol.FindAttribute(IgnoreAttribute);
                var ignoreNever = false;
                if (ignore is not null)
                {
                    var condition = ignore.GetNamedArgument("Condition")?.Value is int c ? c : IgnoreAlways;
                    switch (condition)
                    {
                        case IgnoreAlways:
                            member.Exclude("Excluded by [JsonIgnore].", "[JsonIgnore]");
                            continue;
                        case IgnoreWhenWriting:
                            member.Exclude("Excluded on write by [JsonIgnore(Condition = WhenWriting)].", "[JsonIgnore(Condition = WhenWriting)]");
                            continue;
                        case IgnoreWhenReading:
                            member.Provenance.Add("[JsonIgnore(Condition = WhenReading)] (no effect on write)");
                            break;
                        case IgnoreNever:
                            ignoreNever = true;
                            member.Provenance.Add("[JsonIgnore(Condition = Never)]");
                            member.Explanations.Add("Always written; overrides the global ignore condition.");
                            break;
                        case IgnoreWhenWritingNull:
                            member.Provenance.Add("[JsonIgnore(Condition = WhenWritingNull)]");
                            if (!member.CanBeNull)
                            {
                                Report(DiagnosticIds.InvalidIgnoreCondition, null, member.ClrName, "JsonIgnoreCondition.WhenWritingNull", member.ClrName);
                                member.Downgrade(Classification.Unsupported);
                            }
                            else
                            {
                                member.IgnoreCondition = MemberIgnoreCondition.WhenWritingNull;
                                member.Explanations.Add("Omitted when the value is null.");
                            }

                            break;
                        case IgnoreWhenWritingDefault:
                            member.Provenance.Add("[JsonIgnore(Condition = WhenWritingDefault)]");
                            member.IgnoreCondition = MemberIgnoreCondition.WhenWritingDefault;
                            member.Explanations.Add("Omitted when the value equals its CLR default.");
                            break;
                    }
                }

                if (ignore is null || (!ignoreNever && member.IgnoreCondition == MemberIgnoreCondition.Never && ignore.GetNamedArgument("Condition")?.Value is IgnoreWhenReading))
                {
                    ApplyGlobalIgnore(member);
                }

                if (member.IsReadOnly && !include && !ignoreNever)
                {
                    var readOnlyIgnored = member.MemberKind == MemberKind.Property ? Profile.IgnoreReadOnlyProperties : Profile.IgnoreReadOnlyFields;
                    if (readOnlyIgnored)
                    {
                        member.Exclude(member.MemberKind == MemberKind.Property ? "Read-only property excluded by IgnoreReadOnlyProperties." : "Read-only field excluded by IgnoreReadOnlyFields.");
                        continue;
                    }
                }

                // Extension data.
                if (symbol.HasAttribute(ExtensionDataAttribute))
                {
                    member.Provenance.Add("[JsonExtensionData]");
                    member.IsExtensionData = true;
                    if (hasExtensionData)
                    {
                        Report(DiagnosticIds.InvalidExtensionData, null, member.ClrName, member.ClrName, "duplicate extension data member");
                        member.Downgrade(Classification.Unsupported);
                    }
                    else if (IsValidExtensionDataType(candidate.Type))
                    {
                        Report(DiagnosticIds.ExtensionDataEstimated, null, member.ClrName, member.ClrName);
                        member.Downgrade(Classification.Conditional);
                        member.Explanations.Add("Extension data entries are written as top-level properties of the parent object; they are unknown statically.");
                    }
                    else
                    {
                        Report(DiagnosticIds.InvalidExtensionData, null, member.ClrName, member.ClrName, member.ClrTypeDisplay);
                        member.Downgrade(Classification.Unsupported);
                    }

                    hasExtensionData = true;
                    continue;
                }

                // Name.
                var explicitName = symbol.FindAttribute(PropertyNameAttribute)?.GetConstructorArgument(0)?.Value as string;
                if (explicitName is not null)
                {
                    member.JsonName = explicitName;
                    member.Provenance.Add($"[JsonPropertyName(\"{explicitName}\")]");
                    member.Explanations.Add($"Renamed to \"{explicitName}\" by [JsonPropertyName]; naming policies are not applied.");
                }
                else
                {
                    member.JsonName = ApplyNaming(Profile.PropertyNaming, member.ClrName);
                    if (Profile.PropertyNaming != NamingPolicyKind.Unchanged)
                    {
                        member.Provenance.Add("PropertyNamingPolicy = " + Profile.PropertyNaming);
                    }
                }

                // Order.
                if (symbol.FindAttribute(OrderAttribute)?.GetConstructorArgument(0)?.Value is int order)
                {
                    member.Order = order;
                    member.Provenance.Add($"[JsonPropertyOrder({order})]");
                }

                if (symbol.HasAttribute(RequiredAttribute))
                {
                    member.IsRequired = true;
                    member.Provenance.Add("[JsonRequired] (deserialization metadata)");
                }

                // Number handling.
                var numberHandling = symbol.FindAttribute(NumberHandlingAttribute) ?? typeNumberHandling;
                if (numberHandling?.GetConstructorArgument(0)?.Value is int handling)
                {
                    if ((handling & NumberHandlingWriteAsString) != 0)
                    {
                        member.NumberAsString = true;
                        member.Provenance.Add("[JsonNumberHandling(WriteAsString)]");
                    }
                }
                else if (Profile.WriteNumbersAsStrings)
                {
                    member.NumberAsString = true;
                }

                // Converter and type.
                EnumStyle? enumStyle = null;
                var converterAttribute = symbol.FindAttribute(ConverterAttribute);
                if (converterAttribute is not null)
                {
                    var decision = Recognize(converterAttribute, candidate.Type);
                    member.Converter = decision.DisplayName;
                    member.Provenance.Add($"[JsonConverter(typeof({decision.DisplayName}))]");
                    if (decision.IsKnown)
                    {
                        enumStyle = decision.EnumStyle;
                    }
                    else
                    {
                        Report(DiagnosticIds.UnknownConverter, null, member.ClrName, decision.DisplayName);
                        member.Downgrade(Classification.Unsupported);
                        member.Explanations.Add($"Custom converter {decision.DisplayName} is not modeled; the default representation is shown.");
                    }
                }

                member.Contract = Resolve(candidate.Type, enumStyle);
                if (member.Contract.Classification > Classification.Resolved)
                {
                    member.Downgrade(member.Contract.Classification == Classification.Unsupported ? Classification.Unsupported : Classification.Estimated);
                }
            }

            ConfigurePolymorphism(contract, type);
            PropertiesBeforeFields(contract);
            SortMembers(contract, m => m.Order ?? 0);
            CheckDuplicateNames(contract, contract.Polymorphism?.DiscriminatorPropertyName);
        }

        private void ApplyGlobalIgnore(PropertyContract member)
        {
            switch (Profile.IgnoreCondition)
            {
                case DefaultIgnoreCondition.WhenWritingNull when member.CanBeNull:
                    member.IgnoreCondition = MemberIgnoreCondition.WhenWritingNull;
                    member.Provenance.Add("DefaultIgnoreCondition = WhenWritingNull");
                    member.Explanations.Add("Omitted when the value is null.");
                    break;
                case DefaultIgnoreCondition.WhenWritingDefault:
                    member.IgnoreCondition = MemberIgnoreCondition.WhenWritingDefault;
                    member.Provenance.Add("DefaultIgnoreCondition = WhenWritingDefault");
                    member.Explanations.Add("Omitted when the value equals its CLR default.");
                    break;
            }
        }

        private static bool IsValidExtensionDataType(ITypeSymbol type)
        {
            if (type.Is("System.Text.Json.Nodes.JsonObject"))
            {
                return true;
            }

            if (type is INamedTypeSymbol { IsGenericType: true } named &&
                (named.Is(KnownTypeNames.IDictionary2) || named.AllInterfaces.Any(i => i.Is(KnownTypeNames.IDictionary2))))
            {
                var dictionary = named.Is(KnownTypeNames.IDictionary2) ? named : named.AllInterfaces.First(i => i.Is(KnownTypeNames.IDictionary2));
                return dictionary.TypeArguments[0].SpecialType == SpecialType.System_String &&
                       (dictionary.TypeArguments[1].SpecialType == SpecialType.System_Object || dictionary.TypeArguments[1].Is(KnownTypeNames.JsonElement));
            }

            return false;
        }

        private void ConfigurePolymorphism(ObjectContract contract, INamedTypeSymbol type)
        {
            var derived = type.FindAttributes(DerivedTypeAttribute).ToList();
            var polymorphic = type.FindAttribute(PolymorphicAttribute);
            if (derived.Count == 0 && polymorphic is null)
            {
                return;
            }

            var discriminatorName = polymorphic?.GetNamedArgument("TypeDiscriminatorPropertyName")?.Value as string ?? "$type";
            var info = new PolymorphismInfo(discriminatorName, polymorphic is null ? "[JsonDerivedType]" : "[JsonPolymorphic]");
            var seenDiscriminators = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var attribute in derived)
            {
                if (attribute.GetConstructorArgument(0)?.Value is not INamedTypeSymbol derivedType)
                {
                    continue;
                }

                var discriminatorValue = attribute.GetConstructorArgument(1)?.Value;
                string? discriminatorJson = discriminatorValue switch
                {
                    string s => JsonTextWriter.Quote(s, JsonEscaping.SystemTextJsonDefault),
                    int i => i.ToString(CultureInfo.InvariantCulture),
                    _ => null,
                };

                if (discriminatorJson is not null && !seenDiscriminators.Add(discriminatorJson))
                {
                    Report(DiagnosticIds.DiscriminatorConflict, null, null, type.ToShortDisplay(), $"discriminator {discriminatorJson} is used more than once");
                    contract.Downgrade(Classification.Unsupported);
                }

                if (SymbolEqualityComparer.Default.Equals(derivedType, type))
                {
                    info.BaseDiscriminatorJson = discriminatorJson;
                    continue;
                }

                if (!IsDerivedFrom(derivedType, type))
                {
                    Report(DiagnosticIds.DiscriminatorConflict, null, null, type.ToShortDisplay(), $"{derivedType.ToShortDisplay()} does not derive from the base type");
                    contract.Downgrade(Classification.Unsupported);
                    continue;
                }

                var variant = new PolymorphicVariant(derivedType.ToIdentity(), derivedType.ToFullDisplay(), discriminatorJson);
                info.Variants.Add(variant);
                variant.Contract = Resolve(derivedType, null) as ObjectContract;
                if (variant.Contract is not null && discriminatorJson is not null &&
                    variant.Contract.IncludedMembers.Any(m => m.JsonName == discriminatorName))
                {
                    Report(DiagnosticIds.DiscriminatorConflict, null, null, type.ToShortDisplay(), $"{derivedType.ToShortDisplay()} has a member named '{discriminatorName}'");
                }
            }

            contract.Polymorphism = info;
            contract.Notes.Add($"Polymorphic via {info.Source}; discriminator property '{discriminatorName}'.");
        }

        private static bool IsDerivedFrom(INamedTypeSymbol candidate, INamedTypeSymbol baseType)
        {
            if (baseType.TypeKind == TypeKind.Interface)
            {
                return candidate.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, baseType));
            }

            for (var t = candidate.BaseType; t is not null; t = t.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(t, baseType))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
