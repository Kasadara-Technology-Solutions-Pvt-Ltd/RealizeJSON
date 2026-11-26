using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RealizeJSON.Core;
using RealizeJSON.Core.Contracts;
using RealizeJSON.Core.Json;
using RealizeJSON.Core.Naming;
using RealizeJSON.Core.Samples;

namespace RealizeJSON.SampleData;

/// <summary>
/// Converts a modeled CLR value tree into a JSON DOM using the resolved contract. Write-time omission rules are
/// evaluated on the CLR values. Diagnostics never appear inside the JSON.
/// </summary>
public sealed class JsonEmitter
{
    private readonly ResolvedContract _contract;
    private readonly IJsonValueFormatter _formatter;
    private readonly SerializationProfile _profile;
    private readonly List<PreviewDiagnostic> _diagnostics = new();
    private int _nextReferenceId;

    private JsonEmitter(ResolvedContract contract)
    {
        _contract = contract;
        _formatter = contract.Formatter;
        _profile = contract.Profile;
    }

    /// <summary>Number of JSON properties written for CLR members (used for the status bar).</summary>
    public int MemberPropertyCount { get; private set; }

    public static JsonNodeModel Emit(ResolvedContract contract, SampleValue root) => Emit(contract, root, out _, out _);

    public static JsonNodeModel Emit(ResolvedContract contract, SampleValue root, out int memberPropertyCount) => Emit(contract, root, out memberPropertyCount, out _);

    public static JsonNodeModel Emit(ResolvedContract contract, SampleValue root, out int memberPropertyCount, out IReadOnlyList<PreviewDiagnostic> diagnostics)
    {
        var emitter = new JsonEmitter(contract);
        diagnostics = emitter._diagnostics;
        var node = emitter.EmitValue(contract.Root, root, "$", numberAsString: contract.Profile.WriteNumbersAsStrings && contract.Profile.Serializer == SerializerKind.SystemTextJson);
        memberPropertyCount = emitter.MemberPropertyCount;
        return node;
    }

    private bool PreserveReferences => _profile.ReferenceHandling == ReferenceHandlingMode.Preserve;

    private JsonNodeModel EmitValue(JsonContract contract, SampleValue value, string path, bool numberAsString)
    {
        JsonNodeModel node = value switch
        {
            NullSampleValue => new JsonNullNode(),
            ScalarSampleValue scalar => _formatter.FormatScalar(scalar, numberAsString),
            EnumSampleValue e => EmitEnum(e),
            ObjectSampleValue obj => EmitObject(obj, path),
            CollectionSampleValue collection => EmitCollection(collection, path, numberAsString),
            DictionarySampleValue dictionary => EmitDictionary(dictionary, path, numberAsString),
            AnySampleValue => new JsonObjectNode(),
            _ => new JsonNullNode(),
        };
        node.Path = path;
        return node;
    }

    private JsonNodeModel EmitEnum(EnumSampleValue value)
    {
        var contract = value.Contract;
        if (contract.AsStrings)
        {
            var name = _formatter.FormatEnumName(contract, value.RawValue);
            if (name is not null)
            {
                return new JsonStringNode(name);
            }
        }

        var text = contract.IsSigned
            ? unchecked((long)value.RawValue).ToString(CultureInfo.InvariantCulture)
            : value.RawValue.ToString(CultureInfo.InvariantCulture);
        return new JsonNumberNode(text);
    }

    private JsonObjectNode EmitObject(ObjectSampleValue value, string path)
    {
        var node = new JsonObjectNode { Path = path, Truncated = value.Truncated };
        if (PreserveReferences && (!value.IsValueType || _profile.Serializer == SerializerKind.NewtonsoftJson))
        {
            node.Properties.Add(new JsonPropertyNode("$id", new JsonStringNode(NextId()) { Path = path + ".$id" }, null));
        }

        if (value.Variant?.DiscriminatorJson is { } discriminator && value.Variant.Contract is not null)
        {
            var root = _contract.Root as ObjectContract;
            var name = root?.Polymorphism?.DiscriminatorPropertyName ?? "$type";
            node.Properties.Add(new JsonPropertyNode(name, ParseDiscriminator(discriminator, path + "." + name), null));
        }
        else if (value.Variant is null && value.Contract.Polymorphism?.BaseDiscriminatorJson is { } baseDiscriminator)
        {
            var name = value.Contract.Polymorphism.DiscriminatorPropertyName;
            node.Properties.Add(new JsonPropertyNode(name, ParseDiscriminator(baseDiscriminator, path + "." + name), null));
        }

        foreach (var pair in value.Members)
        {
            var member = pair.Key;
            var memberValue = pair.Value;
            if (ShouldOmit(member, memberValue, _profile.Serializer))
            {
                continue;
            }

            var memberPath = path + FormatPathSegment(member.JsonName);
            if (member.RejectsNullOnWrite && memberValue is NullSampleValue && _diagnostics.Count < 20)
            {
                _diagnostics.Add(Diagnostics.DiagnosticCatalog.Create(Diagnostics.DiagnosticIds.RequiredMemberNull, memberPath, member.ClrName, member.ClrName));
            }

            var child = EmitValue(member.Contract!, memberValue, memberPath, member.NumberAsString);
            node.Properties.Add(new JsonPropertyNode(member.JsonName, child, member));
            MemberPropertyCount++;
        }

        return node;
    }

    private static JsonNodeModel ParseDiscriminator(string json, string path)
    {
        JsonNodeModel node = json.StartsWith("\"", StringComparison.Ordinal)
            ? new JsonStringNode(((JsonStringNode)JsonTextReader.Parse(json)).Value)
            : new JsonNumberNode(json);
        node.Path = path;
        return node;
    }

    /// <summary>Evaluates write-time omission rules against the modeled CLR value.</summary>
    public static bool ShouldOmit(PropertyContract member, SampleValue value, SerializerKind serializer = SerializerKind.SystemTextJson)
    {
        // Newtonsoft.Json's default-value comparison never treats '\0' as the default of char.
        if (serializer == SerializerKind.NewtonsoftJson && member.IgnoreCondition == MemberIgnoreCondition.WhenWritingDefault &&
            !member.HasDefaultValue && value is ScalarSampleValue { Kind: PrimitiveKind.Char })
        {
            return false;
        }

        switch (member.IgnoreCondition)
        {
            case MemberIgnoreCondition.WhenWritingNull:
                return value is NullSampleValue;
            case MemberIgnoreCondition.WhenWritingDefault:
                if (member.HasDefaultValue)
                {
                    return EqualsDefaultValue(value, member.DefaultValue);
                }

                return value.IsClrDefault;
            default:
                return false;
        }
    }

    private static bool EqualsDefaultValue(SampleValue value, object? defaultValue)
    {
        if (defaultValue is null)
        {
            return value is NullSampleValue;
        }

        switch (value)
        {
            case ScalarSampleValue scalar:
                try
                {
                    return scalar.Value switch
                    {
                        string s => string.Equals(s, Convert.ToString(defaultValue, CultureInfo.InvariantCulture), StringComparison.Ordinal),
                        bool b => defaultValue is bool db && b == db,
                        long l => IsNumber(defaultValue) && Convert.ToDecimal(defaultValue, CultureInfo.InvariantCulture) == l,
                        ulong u => IsNumber(defaultValue) && Convert.ToDecimal(defaultValue, CultureInfo.InvariantCulture) == u,
                        decimal m => IsNumber(defaultValue) && Convert.ToDecimal(defaultValue, CultureInfo.InvariantCulture) == m,
                        double d => IsNumber(defaultValue) && Convert.ToDouble(defaultValue, CultureInfo.InvariantCulture).Equals(d),
                        float f => IsNumber(defaultValue) && Convert.ToSingle(defaultValue, CultureInfo.InvariantCulture).Equals(f),
                        char c => defaultValue is char dc && dc == c,
                        _ => false,
                    };
                }
                catch (OverflowException)
                {
                    return false;
                }

            case EnumSampleValue e:
                return IsNumber(defaultValue) && unchecked((ulong)Convert.ToInt64(defaultValue, CultureInfo.InvariantCulture)) == e.RawValue;
            default:
                return false;
        }
    }

    private static bool IsNumber(object o) => o is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private JsonNodeModel EmitCollection(CollectionSampleValue value, string path, bool numberAsString)
    {
        var array = new JsonArrayNode { Path = path, Truncated = value.Truncated };

        // System.Text.Json wraps non-array collections as {"$id","$values"} (ids are assigned before elements).
        var wrapperId = PreserveReferences && _profile.Serializer == SerializerKind.SystemTextJson && value.Contract.Type.Kind != "array"
            ? NextId()
            : null;
        if (value.Dimensions is { Count: > 1 } dims)
        {
            var index = 0;
            BuildNested(array, dims, 0, value, ref index, path, numberAsString);
        }
        else
        {
            for (var i = 0; i < value.Items.Count; i++)
            {
                var itemPath = path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                array.Items.Add(EmitValue(value.Contract.Element, value.Items[i], itemPath, numberAsString));
            }
        }

        if (wrapperId is not null)
        {
            var wrapper = new JsonObjectNode { Path = path };
            wrapper.Properties.Add(new JsonPropertyNode("$id", new JsonStringNode(wrapperId) { Path = path + ".$id" }, null));
            wrapper.Properties.Add(new JsonPropertyNode("$values", array, null));
            return wrapper;
        }

        return array;
    }

    private void BuildNested(JsonArrayNode target, IReadOnlyList<int> dims, int dim, CollectionSampleValue value, ref int index, string path, bool numberAsString)
    {
        for (var i = 0; i < dims[dim]; i++)
        {
            var childPath = path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
            if (dim == dims.Count - 1)
            {
                if (index >= value.Items.Count)
                {
                    target.Truncated = true;
                    return;
                }

                target.Items.Add(EmitValue(value.Contract.Element, value.Items[index++], childPath, numberAsString));
            }
            else
            {
                var child = new JsonArrayNode { Path = childPath };
                BuildNested(child, dims, dim + 1, value, ref index, childPath, numberAsString);
                target.Items.Add(child);
            }
        }
    }

    private JsonObjectNode EmitDictionary(DictionarySampleValue value, string path, bool numberAsString)
    {
        var node = new JsonObjectNode { Path = path };
        if (PreserveReferences)
        {
            node.Properties.Add(new JsonPropertyNode("$id", new JsonStringNode(NextId()) { Path = path + ".$id" }, null));
        }

        var contract = value.Contract;
        foreach (var entry in value.Entries)
        {
            string key = entry.Key switch
            {
                EnumSampleValue e => _formatter.FormatEnumKey(e.Contract, e.RawValue),
                ScalarSampleValue s => _formatter.FormatKey(s),
                _ => "null",
            };

            key = NamingPolicies.Convert(contract.KeyNaming, key, _profile.Serializer);
            var entryPath = path + FormatPathSegment(key);
            node.Properties.Add(new JsonPropertyNode(key, EmitValue(contract.Value, entry.Value, entryPath, numberAsString), null));
        }

        return node;
    }

    private string NextId() => (++_nextReferenceId).ToString(CultureInfo.InvariantCulture);

    /// <summary>Formats a JSON path segment: <c>.name</c> or <c>['odd name']</c>.</summary>
    public static string FormatPathSegment(string name)
    {
        var simple = name.Length > 0 && name.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '$') && !char.IsDigit(name[0]);
        return simple ? "." + name : "['" + name.Replace("\\", "\\\\").Replace("'", "\\'") + "']";
    }
}
