using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using RealizeJSON.CodeAnalysis;
using RealizeJSON.Core;
using RealizeJSON.Core.Contracts;
using RealizeJSON.Core.Json;
using RealizeJSON.Diagnostics;

namespace RealizeJSON.Serialization.NewtonsoftJson;

/// <summary>Newtonsoft.Json rules (DefaultContractResolver semantics, version 13.0).</summary>
public sealed class NewtonsoftJsonRuleProvider : IRuleProvider
{
    public SerializerKind Serializer => SerializerKind.NewtonsoftJson;

    public IJsonValueFormatter Formatter => NewtonsoftJsonFormatter.Instance;

    public ResolvedContract BuildContract(ITypeSymbol root, Compilation compilation, SerializationProfile profile, CancellationToken cancellationToken) =>
        new Builder(compilation, profile, cancellationToken).Build(root, Formatter);

    private enum MemberSerialization
    {
        OptOut = 0,
        OptIn = 1,
        Fields = 2,
    }

    private sealed class Builder : ContractBuilderBase
    {
        private const string Ns = "Newtonsoft.Json.";
        private const string PropertyAttribute = Ns + "JsonPropertyAttribute";
        private const string IgnoreAttribute = Ns + "JsonIgnoreAttribute";
        private const string ObjectAttribute = Ns + "JsonObjectAttribute";
        private const string RequiredAttribute = Ns + "JsonRequiredAttribute";
        private const string ExtensionDataAttribute = Ns + "JsonExtensionDataAttribute";
        private const string ConverterAttribute = Ns + "JsonConverterAttribute";
        private const string ConstructorAttribute = Ns + "JsonConstructorAttribute";
        private const string StringEnumConverter = Ns + "Converters.StringEnumConverter";
        private const string IsoDateTimeConverter = Ns + "Converters.IsoDateTimeConverter";
        private const string VersionConverter = Ns + "Converters.VersionConverter";
        private const string JavaScriptDateTimeConverter = Ns + "Converters.JavaScriptDateTimeConverter";
        private const string CamelCaseStrategy = Ns + "Serialization.CamelCaseNamingStrategy";
        private const string SnakeCaseStrategy = Ns + "Serialization.SnakeCaseNamingStrategy";
        private const string KebabCaseStrategy = Ns + "Serialization.KebabCaseNamingStrategy";
        private const string TypeConverterAttribute = "System.ComponentModel.TypeConverterAttribute";
        private const string OnSerializingAttribute = "System.Runtime.Serialization.OnSerializingAttribute";
        private const string OnSerializedAttribute = "System.Runtime.Serialization.OnSerializedAttribute";

        // Newtonsoft.Json enums.
        private const int NullValueIgnore = 1;
        private const int DefaultValueIgnoreFlag = 1;
        private const int RequiredAllowNull = 1;
        private const int RequiredAlways = 2;
        private const int RequiredDisallowNull = 3;

        public Builder(Compilation compilation, SerializationProfile profile, CancellationToken cancellationToken)
            : base(compilation, profile, cancellationToken)
        {
        }

        protected override SerializerKind Serializer => SerializerKind.NewtonsoftJson;

        protected override ConverterDecision? GetTypeConverter(INamedTypeSymbol type)
        {
            var attribute = type.FindAttribute(ConverterAttribute);
            if (attribute is not null)
            {
                return Recognize(attribute, type, attribute.GetConstructorArgument(0)?.Value as INamedTypeSymbol, ExtraArguments(attribute));
            }

            var typeConverter = type.FindAttribute(TypeConverterAttribute);
            if (typeConverter is not null && type.TypeKind != TypeKind.Enum)
            {
                var name = (typeConverter.GetConstructorArgument(0)?.Value as ITypeSymbol)?.ToShortDisplay() ?? "TypeConverter";
                return new ConverterDecision($"[TypeConverter({name})] (string conversion)", IsKnown: false);
            }

            return null;
        }

        private static IReadOnlyList<TypedConstant> ExtraArguments(AttributeData attribute)
        {
            var second = attribute.GetConstructorArgument(1);
            return second is { Kind: TypedConstantKind.Array } array ? array.Values : Array.Empty<TypedConstant>();
        }

        private ConverterDecision Recognize(AttributeData? attribute, ITypeSymbol target, INamedTypeSymbol? converterType, IReadOnlyList<TypedConstant> args)
        {
            if (converterType is null)
            {
                return new ConverterDecision(attribute?.AttributeClass?.ToShortDisplay() ?? "JsonConverter", IsKnown: false);
            }

            var display = converterType.ToShortDisplay();
            var name = converterType.OriginalDefinition.GetFullMetadataName();
            var isEnum = (target.TryGetNullableUnderlying(out var u) ? u : target).TypeKind == TypeKind.Enum;
            if (name == StringEnumConverter)
            {
                var naming = NamingPolicyKind.Unchanged;
                foreach (var arg in args)
                {
                    if (arg.Value is true)
                    {
                        naming = NamingPolicyKind.CamelCase;
                    }
                    else if (arg.Value is INamedTypeSymbol strategy)
                    {
                        naming = strategy.GetFullMetadataName() switch
                        {
                            CamelCaseStrategy => NamingPolicyKind.CamelCase,
                            SnakeCaseStrategy => NamingPolicyKind.SnakeCaseLower,
                            KebabCaseStrategy => NamingPolicyKind.KebabCaseLower,
                            _ => naming,
                        };
                    }
                }

                return new ConverterDecision(display, isEnum, new EnumStyle(true, naming, $"StringEnumConverter ({display})"));
            }

            if (name == IsoDateTimeConverter || name == VersionConverter)
            {
                return new ConverterDecision(display, IsKnown: true);
            }

            if (name == JavaScriptDateTimeConverter)
            {
                return new ConverterDecision(display, IsKnown: false) { InvalidOutputReason = "new Date(...) constructor syntax" };
            }

            return new ConverterDecision(display, IsKnown: false);
        }

        protected override string? GetUnsupportedPrimitiveReason(PrimitiveKind kind) => kind switch
        {
            PrimitiveKind.Int128 or PrimitiveKind.UInt128 => "Int128/UInt128 have no built-in Newtonsoft.Json converter",
            _ => null,
        };

        protected override string GetEnumMemberWireName(IFieldSymbol field, EnumStyle style)
        {
            var specified = field.FindAttribute(KnownTypeNames.EnumMemberAttribute)?.GetNamedArgument("Value")?.Value as string;
            if (specified is not null)
            {
                return Profile.OverrideSpecifiedNames ? ApplyNaming(style.Naming, specified) : specified;
            }

            return ApplyNaming(style.Naming, field.Name);
        }

        protected override string? GetEnumMemberKeyName(IFieldSymbol field) =>
            field.FindAttribute(KnownTypeNames.EnumMemberAttribute)?.GetNamedArgument("Value")?.Value as string ?? field.Name;

        protected override JsonContract CreateMultiDimensionalArray(IArrayTypeSymbol array, TypeIdentity identity) =>
            CreateCollection(identity, array.ElementType, null, requiresUnique: false, rank: array.Rank);

        protected override bool IsSupportedDictionaryKey(JsonContract key, out NamingPolicyKind keyNaming)
        {
            keyNaming = Profile.DictionaryKeyNaming;
            if (key is ObjectContract or CollectionContract or AnyContract)
            {
                key.Notes.Add("Newtonsoft.Json converts complex keys with TypeConverter/ToString; the key text is unknown statically.");
                key.Downgrade(Classification.Estimated);
            }

            return key is not UnsupportedContract;
        }

        protected override void PopulateObject(ObjectContract contract, INamedTypeSymbol type)
        {
            var mode = GetMemberSerialization(type, out var modeSource);
            var isDataContract = type.HasAttribute(KnownTypeNames.DataContractAttribute);
            if (contract.IsAbstract)
            {
                Report(DiagnosticIds.UnresolvedRuntimeType, null, null, type.ToShortDisplay(), "Newtonsoft.Json writes the runtime type's members; the declared members are shown as an estimate.");
                contract.Downgrade(Classification.Conditional);
            }

            var callbacks = type.GetMembers().OfType<IMethodSymbol>().Where(m => m.HasAttribute(OnSerializingAttribute) || m.HasAttribute(OnSerializedAttribute)).Select(m => m.Name).ToList();
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

            if (modeSource is not null)
            {
                contract.Notes.Add($"Member serialization: {mode} ({modeSource}).");
            }

            var candidates = CollectMembers(type);
            if (mode == MemberSerialization.Fields)
            {
                candidates = WithBackingFields(type, candidates);
            }

            foreach (var candidate in candidates)
            {
                var member = CreateMember(candidate);
                contract.Members.Add(member);
                PopulateMember(member, candidate, type, mode, isDataContract);
            }

            FieldsBeforeProperties(contract);
            SortMembers(contract, m => m.Order ?? -1);
            CheckDuplicateNames(contract);
        }

        private void PopulateMember(PropertyContract member, MemberCandidate candidate, INamedTypeSymbol type, MemberSerialization mode, bool isDataContract)
        {
            var symbol = candidate.Symbol;
            if (candidate.HiddenBy is not null)
            {
                member.Exclude($"Hidden by a member with the same name in {candidate.HiddenBy}.");
                return;
            }

            var jsonProperty = symbol.FindAttribute(PropertyAttribute);
            var dataMember = isDataContract ? symbol.FindAttribute(KnownTypeNames.DataMemberAttribute) : null;
            var jsonRequired = symbol.FindAttribute(RequiredAttribute);
            var hasMemberAttribute = jsonProperty is not null || dataMember is not null || jsonRequired is not null;
            var isPublic = symbol.DeclaredAccessibility == Accessibility.Public;
            var isBackingField = candidate.Field is { } bf && bf.AssociatedSymbol is not null;

            // Member selection (DefaultContractResolver.GetSerializableMembers).
            if (mode == MemberSerialization.Fields)
            {
                if (candidate.Property is not null && !hasMemberAttribute)
                {
                    member.Exclude("Properties are not serialized in MemberSerialization.Fields mode.");
                    return;
                }
            }
            else if (!isPublic && !hasMemberAttribute)
            {
                member.Exclude("Non-public members are serialized only with [JsonProperty], [JsonRequired] or [DataMember].");
                return;
            }

            if (mode == MemberSerialization.OptIn && !hasMemberAttribute)
            {
                member.Exclude(isDataContract ? "Opt-in ([DataContract]): members require [DataMember] or [JsonProperty]." : "Opt-in ([JsonObject(MemberSerialization.OptIn)]): members require [JsonProperty].");
                return;
            }

            // Ignore attributes.
            if (symbol.HasAttribute(IgnoreAttribute))
            {
                member.Exclude("Excluded by [JsonIgnore].", "[JsonIgnore]");
                return;
            }

            if (candidate.Field is not null && symbol.HasAttribute(KnownTypeNames.NonSerializedAttribute))
            {
                member.Exclude("Excluded by [NonSerialized].", "[NonSerialized]");
                return;
            }

            if (mode != MemberSerialization.OptIn && symbol.HasAttribute(KnownTypeNames.IgnoreDataMemberAttribute))
            {
                member.Exclude("Excluded by [IgnoreDataMember].", "[IgnoreDataMember]");
                return;
            }

            // Readability.
            if (candidate.Property is { } property)
            {
                member.IsReadOnly = property.SetMethod is null;
                if (property.GetMethod is null)
                {
                    member.Exclude("Write-only properties are not serialized.");
                    return;
                }

                if (property.GetMethod.DeclaredAccessibility != Accessibility.Public && !hasMemberAttribute)
                {
                    member.Exclude("The getter is not public.");
                    return;
                }
            }
            else if (candidate.Field is { } field)
            {
                member.IsReadOnly = field.IsReadOnly;
            }

            if (symbol.HasAttribute(ExtensionDataAttribute))
            {
                member.IsExtensionData = true;
                member.Provenance.Add("[JsonExtensionData]");
                Report(DiagnosticIds.ExtensionDataEstimated, null, member.ClrName, member.ClrName);
                member.Downgrade(Classification.Conditional);
                member.Explanations.Add("Extension data entries are written as top-level properties of the parent object; they are unknown statically.");
                return;
            }

            // Name.
            string? specified = null;
            if (dataMember?.GetNamedArgument("Name")?.Value is string dataName)
            {
                specified = dataName;
                member.Provenance.Add($"[DataMember(Name = \"{dataName}\")]");
            }

            if (jsonProperty is not null)
            {
                var name = jsonProperty.GetConstructorArgument(0)?.Value as string ?? jsonProperty.GetNamedArgument("PropertyName")?.Value as string;
                if (name is not null)
                {
                    specified = name;
                    member.Provenance.Add($"[JsonProperty(\"{name}\")]");
                }
                else
                {
                    member.Provenance.Add("[JsonProperty]");
                }
            }

            if (dataMember is not null && specified is null)
            {
                member.Provenance.Add("[DataMember]");
            }

            if (specified is not null)
            {
                member.JsonName = Profile.OverrideSpecifiedNames ? ApplyNaming(Profile.PropertyNaming, specified) : specified;
                member.Explanations.Add(Profile.OverrideSpecifiedNames
                    ? $"Specified name \"{specified}\" is transformed because OverrideSpecifiedNames is enabled."
                    : $"Renamed to \"{specified}\"; the naming strategy does not override specified names.");
            }
            else
            {
                member.JsonName = ApplyNaming(Profile.PropertyNaming, member.ClrName);
                if (Profile.PropertyNaming != NamingPolicyKind.Unchanged)
                {
                    member.Provenance.Add("NamingStrategy = " + Profile.PropertyNaming);
                }
            }

            // Order.
            if (jsonProperty?.GetNamedArgument("Order")?.Value is int order)
            {
                member.Order = order;
                member.Provenance.Add($"Order = {order}");
            }
            else if (dataMember?.GetNamedArgument("Order")?.Value is int dataOrder && dataOrder >= 0)
            {
                member.Order = dataOrder;
                member.Provenance.Add($"DataMember Order = {dataOrder}");
            }

            // Required metadata.
            if (jsonRequired is not null)
            {
                member.IsRequired = true;
                member.RejectsNullOnWrite = true;
                member.Provenance.Add("[JsonRequired]");
            }
            else if (jsonProperty?.GetNamedArgument("Required")?.Value is int required && required != 0)
            {
                member.IsRequired = true;
                member.RejectsNullOnWrite = required is RequiredAlways or RequiredDisallowNull;
                member.Provenance.Add("Required = " + (required switch { RequiredAllowNull => "AllowNull", RequiredAlways => "Always", _ => "DisallowNull" }));
            }
            else if (dataMember?.GetNamedArgument("IsRequired")?.Value is true)
            {
                member.IsRequired = true;
                member.Provenance.Add("DataMember IsRequired");
            }

            // Null/default handling.
            var defaultHandling = jsonProperty?.GetNamedArgument("DefaultValueHandling")?.Value as int?
                ?? (dataMember?.GetNamedArgument("EmitDefaultValue")?.Value is false ? DefaultValueIgnoreFlag : (int?)null);
            var nullHandling = jsonProperty?.GetNamedArgument("NullValueHandling")?.Value as int?;
            var defaultValueAttribute = symbol.FindAttribute(KnownTypeNames.DefaultValueAttribute);
            if (defaultValueAttribute is not null)
            {
                member.HasDefaultValue = true;
                var arg = defaultValueAttribute.GetConstructorArgument(0);
                member.DefaultValue = arg is { Kind: TypedConstantKind.Enum } ? Convert.ToInt64(arg.Value.Value, System.Globalization.CultureInfo.InvariantCulture) : arg?.Value;
                member.Provenance.Add("[DefaultValue]");
            }

            var ignoreDefault = defaultHandling is { } dh ? (dh & DefaultValueIgnoreFlag) != 0 : Profile.IgnoreCondition == DefaultIgnoreCondition.WhenWritingDefault;
            var ignoreNull = nullHandling is { } nh ? nh == NullValueIgnore : Profile.IgnoreCondition == DefaultIgnoreCondition.WhenWritingNull;
            if (ignoreDefault)
            {
                member.IgnoreCondition = MemberIgnoreCondition.WhenWritingDefault;
                member.Provenance.Add(defaultHandling is null ? "DefaultValueHandling = Ignore (settings)" : "DefaultValueHandling = Ignore");
                member.Explanations.Add(member.HasDefaultValue ? "Omitted when the value equals [DefaultValue]." : "Omitted when the value equals its CLR default.");
            }
            else if (ignoreNull)
            {
                member.IgnoreCondition = MemberIgnoreCondition.WhenWritingNull;
                member.Provenance.Add(nullHandling is null ? "NullValueHandling = Ignore (settings)" : "NullValueHandling = Ignore");
                member.Explanations.Add("Omitted when the value is null.");
            }

            // Runtime-conditional inclusion.
            var conditions = new List<string>();
            if (HasMethodNamed(type, "ShouldSerialize" + member.ClrName))
            {
                conditions.Add($"ShouldSerialize{member.ClrName}()");
            }

            if (type.SelfAndBaseTypes().Any(t => t.GetMembers(member.ClrName + "Specified").OfType<IPropertySymbol>().Any(p => p.Type.SpecialType == SpecialType.System_Boolean)))
            {
                conditions.Add($"{member.ClrName}Specified");
            }

            if (conditions.Count > 0)
            {
                member.IgnoreCondition = MemberIgnoreCondition.Conditional;
                member.Downgrade(Classification.Conditional);
                Report(DiagnosticIds.ConditionalMember, null, member.ClrName, member.ClrName, string.Join(", ", conditions));
                member.Explanations.Add("Inclusion depends on " + string.Join(" and ", conditions) + ".");
            }

            // Converters.
            EnumStyle? enumStyle = null;
            var converterAttribute = symbol.FindAttribute(ConverterAttribute);
            var itemConverterType = jsonProperty?.GetNamedArgument("ItemConverterType")?.Value as INamedTypeSymbol;
            if (converterAttribute is not null)
            {
                var decision = Recognize(converterAttribute, candidate.Type, converterAttribute.GetConstructorArgument(0)?.Value as INamedTypeSymbol, ExtraArguments(converterAttribute));
                ApplyMemberConverter(member, decision, ref enumStyle);
            }

            if (itemConverterType is not null)
            {
                var elementType = GetElementType(candidate.Type);
                var decision = Recognize(null, elementType ?? candidate.Type, itemConverterType, Array.Empty<TypedConstant>());
                ApplyMemberConverter(member, decision, ref enumStyle);
            }

            member.Contract = Resolve(candidate.Type, enumStyle);
            if (member.Contract.Classification > Classification.Resolved)
            {
                member.Downgrade(member.Contract.Classification == Classification.Unsupported ? Classification.Unsupported : Classification.Estimated);
            }

            if (isBackingField)
            {
                member.Explanations.Add("Compiler-generated backing field serialized in MemberSerialization.Fields mode.");
            }
        }

        private void ApplyMemberConverter(PropertyContract member, ConverterDecision decision, ref EnumStyle? enumStyle)
        {
            member.Converter = decision.DisplayName;
            member.Provenance.Add($"Converter {decision.DisplayName}");
            if (decision.InvalidOutputReason is not null)
            {
                Report(DiagnosticIds.UnsupportedSerializerOutput, null, member.ClrName, member.ClrName, decision.InvalidOutputReason);
                member.Downgrade(Classification.Unsupported);
                return;
            }

            if (decision.IsKnown)
            {
                enumStyle = decision.EnumStyle ?? enumStyle;
                return;
            }

            Report(DiagnosticIds.UnknownConverter, null, member.ClrName, decision.DisplayName);
            member.Downgrade(Classification.Unsupported);
            member.Explanations.Add($"Custom converter {decision.DisplayName} is not modeled; the default representation is shown.");
        }

        private static ITypeSymbol? GetElementType(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol array)
            {
                return array.ElementType;
            }

            if (type is INamedTypeSymbol named)
            {
                var enumerable = named.AllInterfaces.Concat(new[] { named })
                    .FirstOrDefault(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
                return enumerable?.TypeArguments[0];
            }

            return null;
        }

        private static MemberSerialization GetMemberSerialization(INamedTypeSymbol type, out string? source)
        {
            var jsonObject = type.FindAttribute(ObjectAttribute);
            if (jsonObject is not null)
            {
                var value = jsonObject.GetNamedArgument("MemberSerialization")?.Value ?? jsonObject.GetConstructorArgument(0)?.Value;
                if (value is int mode)
                {
                    source = "[JsonObject]";
                    return (MemberSerialization)mode;
                }
            }

            if (type.HasAttribute(KnownTypeNames.DataContractAttribute))
            {
                source = "[DataContract]";
                return MemberSerialization.OptIn;
            }

            source = null;
            return MemberSerialization.OptOut;
        }

        /// <summary>Fields mode serializes every instance field, including compiler-generated backing fields.</summary>
        private IReadOnlyList<MemberCandidate> WithBackingFields(INamedTypeSymbol type, IReadOnlyList<MemberCandidate> candidates)
        {
            var list = candidates.ToList();
            foreach (var declaring in type.SelfAndBaseTypes().Where(t => t.SpecialType is not (SpecialType.System_Object or SpecialType.System_ValueType)))
            {
                foreach (var field in declaring.GetMembers().OfType<IFieldSymbol>())
                {
                    if (!field.IsStatic && field.AssociatedSymbol is IPropertySymbol)
                    {
                        list.Add(new MemberCandidate(field, field.Type, declaring, null));
                    }
                }
            }

            return list;
        }
    }
}
