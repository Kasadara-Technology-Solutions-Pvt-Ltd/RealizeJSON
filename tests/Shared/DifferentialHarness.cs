using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Newtonsoft.Json.Serialization;
using RealizeJSON.Core;
using RealizeJSON.Core.Contracts;
using RealizeJSON.Core.Samples;
using N = Newtonsoft.Json;

namespace RealizeJSON.Tests.Shared;

/// <summary>
/// Differential testing: the fixture is compiled and loaded into an isolated, collectible load context (tests only),
/// the modeled CLR value tree is materialized through reflection without running constructors or initializers,
/// and the real serializer output is compared with the static preview.
/// </summary>
public sealed class DifferentialHarness : IDisposable
{
    private readonly AssemblyLoadContext _context;

    public DifferentialHarness(Compilation compilation)
    {
        Compilation = compilation;
        _context = new AssemblyLoadContext("fixture-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        _context.Resolving += (_, name) => name.Name == "Newtonsoft.Json" ? typeof(N.JsonConvert).Assembly : null;
        Assembly = _context.LoadFromStream(new System.IO.MemoryStream(TestCompilation.Emit(compilation)));
    }

    public Compilation Compilation { get; }

    public Assembly Assembly { get; }

    public static (string Expected, string Actual, HarnessResult Preview) Compare(string source, string typeName, SerializationProfile profile, PreviewOptions? options = null)
    {
        var compilation = TestCompilation.Create(source);
        using var harness = new DifferentialHarness(compilation);
        var preview = PreviewHarness.Run(compilation, TestCompilation.GetType(compilation, typeName), profile, options);
        var runtimeType = harness.Assembly.GetType(typeName, throwOnError: true)!;
        var instance = harness.Materialize(preview.Samples.Root, runtimeType);
        var expected = Serialize(instance, runtimeType, profile);
        return (expected, preview.Json, preview);
    }

    public static string Serialize(object? instance, Type declaredType, SerializationProfile profile)
    {
        var text = profile.Serializer == SerializerKind.SystemTextJson
            ? JsonSerializer.Serialize(instance, declaredType, StjOptions(profile))
            : N.JsonConvert.SerializeObject(instance, declaredType, NewtonsoftSettings(profile));
        return text.Replace("\r\n", "\n");
    }

    public static JsonSerializerOptions StjOptions(SerializationProfile p)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = StjPolicy(p.PropertyNaming),
            DictionaryKeyPolicy = StjPolicy(p.DictionaryKeyNaming),
            DefaultIgnoreCondition = p.IgnoreCondition switch
            {
                DefaultIgnoreCondition.WhenWritingNull => JsonIgnoreCondition.WhenWritingNull,
                DefaultIgnoreCondition.WhenWritingDefault => JsonIgnoreCondition.WhenWritingDefault,
                _ => JsonIgnoreCondition.Never,
            },
            IncludeFields = p.IncludeFields,
            IgnoreReadOnlyProperties = p.IgnoreReadOnlyProperties,
            IgnoreReadOnlyFields = p.IgnoreReadOnlyFields,
            NumberHandling = p.WriteNumbersAsStrings ? JsonNumberHandling.WriteAsString : JsonNumberHandling.Strict,
            WriteIndented = p.WriteIndented,
            NewLine = "\n",
            ReferenceHandler = p.ReferenceHandling switch
            {
                ReferenceHandlingMode.Preserve => ReferenceHandler.Preserve,
                ReferenceHandlingMode.IgnoreCycles => ReferenceHandler.IgnoreCycles,
                _ => null,
            },
        };

        if (p.Enums == EnumRepresentation.Strings)
        {
            options.Converters.Add(new JsonStringEnumConverter(StjPolicy(p.EnumNaming)));
        }

        return options;
    }

    public static N.JsonSerializerSettings NewtonsoftSettings(SerializationProfile p)
    {
        NamingStrategy strategy = p.PropertyNaming switch
        {
            NamingPolicyKind.CamelCase => new CamelCaseNamingStrategy(),
            NamingPolicyKind.SnakeCaseLower => new SnakeCaseNamingStrategy(),
            NamingPolicyKind.KebabCaseLower => new KebabCaseNamingStrategy(),
            _ => new DefaultNamingStrategy(),
        };
        strategy.ProcessDictionaryKeys = p.DictionaryKeyNaming != NamingPolicyKind.Unchanged;
        strategy.OverrideSpecifiedNames = p.OverrideSpecifiedNames;

        var settings = new N.JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = strategy },
            NullValueHandling = p.IgnoreCondition == DefaultIgnoreCondition.WhenWritingNull ? N.NullValueHandling.Ignore : N.NullValueHandling.Include,
            DefaultValueHandling = p.IgnoreCondition == DefaultIgnoreCondition.WhenWritingDefault ? N.DefaultValueHandling.Ignore : N.DefaultValueHandling.Include,
            Formatting = p.WriteIndented ? N.Formatting.Indented : N.Formatting.None,
            ReferenceLoopHandling = p.ReferenceHandling == ReferenceHandlingMode.IgnoreCycles ? N.ReferenceLoopHandling.Ignore : N.ReferenceLoopHandling.Error,
            PreserveReferencesHandling = p.ReferenceHandling == ReferenceHandlingMode.Preserve ? N.PreserveReferencesHandling.Objects : N.PreserveReferencesHandling.None,
        };

        if (p.Enums == EnumRepresentation.Strings)
        {
            NamingStrategy enumStrategy = p.EnumNaming switch
            {
                NamingPolicyKind.CamelCase => new CamelCaseNamingStrategy(),
                NamingPolicyKind.SnakeCaseLower => new SnakeCaseNamingStrategy(),
                NamingPolicyKind.KebabCaseLower => new KebabCaseNamingStrategy(),
                _ => new DefaultNamingStrategy(),
            };
            settings.Converters.Add(new N.Converters.StringEnumConverter(enumStrategy));
        }

        return settings;
    }

    private static JsonNamingPolicy? StjPolicy(NamingPolicyKind kind) => kind switch
    {
        NamingPolicyKind.CamelCase => JsonNamingPolicy.CamelCase,
        NamingPolicyKind.SnakeCaseLower => JsonNamingPolicy.SnakeCaseLower,
        NamingPolicyKind.SnakeCaseUpper => JsonNamingPolicy.SnakeCaseUpper,
        NamingPolicyKind.KebabCaseLower => JsonNamingPolicy.KebabCaseLower,
        NamingPolicyKind.KebabCaseUpper => JsonNamingPolicy.KebabCaseUpper,
        _ => null,
    };

    /// <summary>Materializes a modeled value without running constructors, initializers or property getters.</summary>
    public object? Materialize(SampleValue value, Type target)
    {
        var underlying = Nullable.GetUnderlyingType(target) ?? target;
        switch (value)
        {
            case NullSampleValue:
                return null;
            case ScalarSampleValue scalar:
                return ConvertScalar(scalar, underlying);
            case EnumSampleValue e:
                return Enum.ToObject(underlying, e.Contract.IsSigned ? (object)unchecked((long)e.RawValue) : e.RawValue);
            case ObjectSampleValue obj:
                return MaterializeObject(obj, underlying);
            case CollectionSampleValue collection:
                return MaterializeCollection(collection, underlying);
            case DictionarySampleValue dictionary:
                return MaterializeDictionary(dictionary, underlying);
            case AnySampleValue:
                return underlying == typeof(object) ? new object() : underlying.FullName switch
                {
                    "System.Text.Json.JsonElement" => JsonDocument.Parse("{}").RootElement,
                    _ => Activator.CreateInstance(underlying),
                };
            default:
                throw new NotSupportedException(value.GetType().Name);
        }
    }

    private static object ConvertScalar(ScalarSampleValue scalar, Type target)
    {
        var v = scalar.Value;
        return scalar.Kind switch
        {
            PrimitiveKind.Uri => new Uri((string)v),
            PrimitiveKind.Version => Version.Parse((string)v),
            PrimitiveKind.Int128 => Int128.Parse((string)v, CultureInfo.InvariantCulture),
            PrimitiveKind.UInt128 => UInt128.Parse((string)v, CultureInfo.InvariantCulture),
            PrimitiveKind.BigInteger => BigInteger.Parse((string)v, CultureInfo.InvariantCulture),
            PrimitiveKind.Half => (Half)(double)v,
            PrimitiveKind.DateOnly => new DateOnly(((DateOnlyValue)v).Year, ((DateOnlyValue)v).Month, ((DateOnlyValue)v).Day),
            PrimitiveKind.TimeOnly => new TimeOnly(((TimeOnlyValue)v).Ticks),
            _ when v.GetType() == target => v,
            _ => Convert.ChangeType(v, target, CultureInfo.InvariantCulture),
        };
    }

    private object MaterializeObject(ObjectSampleValue obj, Type target)
    {
        var runtimeType = target;
        if (obj.Variant is not null || target.IsAbstract || target.IsInterface)
        {
            var name = obj.Contract.Type.FullName;
            runtimeType = Assembly.GetTypes().FirstOrDefault(t => CSharpName(t) == name) ?? target;
        }

        var instance = RuntimeHelpers.GetUninitializedObject(runtimeType);
        foreach (var pair in obj.Members)
        {
            var member = pair.Key;
            var declaring = FindDeclaringType(runtimeType, member.DeclaringType);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            if (member.MemberKind == MemberKind.Field)
            {
                var field = declaring.GetField(member.ClrName, flags) ?? throw new MissingMemberException(member.DeclaringType, member.ClrName);
                field.SetValue(instance, Materialize(pair.Value, field.FieldType));
                continue;
            }

            var property = declaring.GetProperty(member.ClrName, flags) ?? throw new MissingMemberException(member.DeclaringType, member.ClrName);
            var materialized = Materialize(pair.Value, property.PropertyType);
            var camel = char.ToLowerInvariant(member.ClrName[0]) + member.ClrName.Substring(1);
            var backing = declaring.GetField("<" + member.ClrName + ">k__BackingField", flags)
                ?? declaring.GetField("m_" + member.ClrName, flags)
                ?? declaring.GetField(camel, flags)
                ?? declaring.GetField("_" + camel, flags);
            if (backing is not null)
            {
                backing.SetValue(instance, materialized);
            }
            else if (property.SetMethod is not null)
            {
                property.SetValue(instance, materialized);
            }
            else
            {
                throw new InvalidOperationException($"Cannot assign {member.ClrName} without running user code.");
            }
        }

        return instance;
    }

    private static Type FindDeclaringType(Type runtimeType, string declaringFullName)
    {
        for (var t = runtimeType; t is not null; t = t.BaseType)
        {
            if (CSharpName(t) == declaringFullName || CSharpName(t.IsGenericType ? t : t) == declaringFullName)
            {
                return t;
            }
        }

        return runtimeType;
    }

    /// <summary>Approximates Roslyn's fully qualified C# display (without global::) for runtime types.</summary>
    public static string CSharpName(Type t)
    {
        if (t.IsGenericParameter)
        {
            return t.Name;
        }

        var ns = t.Namespace is null ? string.Empty : t.Namespace + ".";
        var prefix = t.DeclaringType is not null && !t.IsGenericParameter ? CSharpName(t.DeclaringType).Split('<')[0] + "." : ns;
        var name = t.Name.Split('`')[0];
        if (!t.IsGenericType)
        {
            return Keyword(t) ?? prefix + name;
        }

        var args = t.GetGenericArguments().Skip(t.DeclaringType?.GetGenericArguments().Length ?? 0).Select(CSharpName);
        return prefix + name + "<" + string.Join(", ", args) + ">";
    }

    private static string? Keyword(Type t) => Type.GetTypeCode(t) switch
    {
        TypeCode.Int32 => "int",
        TypeCode.String => "string",
        TypeCode.Boolean => "bool",
        TypeCode.Int64 => "long",
        TypeCode.Double => "double",
        TypeCode.Decimal => "decimal",
        _ => t == typeof(object) ? "object" : null,
    };

    private object MaterializeCollection(CollectionSampleValue collection, Type target)
    {
        var elementType = target.IsArray ? target.GetElementType()! : GetEnumerableElement(target);
        var items = collection.Items.Select(i => Materialize(i, elementType)).ToList();
        if (target.IsArray)
        {
            if (collection.Dimensions is { Count: > 1 } dims)
            {
                var array = Array.CreateInstance(elementType, dims.ToArray());
                var indices = new int[dims.Count];
                foreach (var item in items)
                {
                    array.SetValue(item, indices);
                    for (var d = dims.Count - 1; d >= 0; d--)
                    {
                        if (++indices[d] < dims[d])
                        {
                            break;
                        }

                        indices[d] = 0;
                    }
                }

                return array;
            }

            var result = Array.CreateInstance(elementType, items.Count);
            for (var i = 0; i < items.Count; i++)
            {
                result.SetValue(items[i], i);
            }

            return result;
        }

        Type concrete;
        if (target.IsInterface)
        {
            var isSet = target.IsGenericType && (target.GetGenericTypeDefinition() == typeof(ISet<>) || target.GetGenericTypeDefinition() == typeof(IReadOnlySet<>));
            concrete = (isSet ? typeof(HashSet<>) : typeof(List<>)).MakeGenericType(elementType);
        }
        else
        {
            concrete = target;
        }

        var instance = Activator.CreateInstance(concrete)!;
        var add = concrete.GetMethod("Add", new[] { elementType }) ?? throw new MissingMethodException(concrete.Name, "Add");
        foreach (var item in items)
        {
            add.Invoke(instance, new[] { item });
        }

        return instance;
    }

    private object MaterializeDictionary(DictionarySampleValue dictionary, Type target)
    {
        var args = GetDictionaryArguments(target);
        var concrete = target.IsInterface ? typeof(Dictionary<,>).MakeGenericType(args) : target;
        var instance = (IDictionary)Activator.CreateInstance(concrete)!;
        foreach (var entry in dictionary.Entries)
        {
            instance.Add(Materialize(entry.Key, args[0])!, Materialize(entry.Value, args[1]));
        }

        return instance;
    }

    private static Type GetEnumerableElement(Type type)
    {
        var enumerable = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            ? type
            : type.GetInterfaces().First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return enumerable.GetGenericArguments()[0];
    }

    private static Type[] GetDictionaryArguments(Type type)
    {
        var candidates = new[] { type }.Concat(type.GetInterfaces());
        var dictionary = candidates.First(i => i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(IDictionary<,>) || i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));
        return dictionary.GetGenericArguments();
    }

    public void Dispose() => _context.Unload();
}
