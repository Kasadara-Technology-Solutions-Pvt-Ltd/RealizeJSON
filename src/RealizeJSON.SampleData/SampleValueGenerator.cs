using System;
using System.Collections.Generic;
using System.Linq;
using RealizeJSON.Core;
using RealizeJSON.Core.Contracts;
using RealizeJSON.Core.Samples;
using RealizeJSON.Diagnostics;

namespace RealizeJSON.SampleData;

/// <summary>Result of sample value generation.</summary>
public sealed class SampleGenerationResult
{
    public SampleGenerationResult(SampleValue root, IReadOnlyList<PreviewDiagnostic> diagnostics, bool incomplete)
    {
        Root = root;
        Diagnostics = diagnostics;
        Incomplete = incomplete;
    }

    public SampleValue Root { get; }

    public IReadOnlyList<PreviewDiagnostic> Diagnostics { get; }

    public bool Incomplete { get; }
}

/// <summary>
/// Generates a modeled CLR value tree for a resolved contract. Deterministic, bounded by depth and node limits,
/// and cycle-safe (recursion is detected on the current traversal path; repeated sibling types are not cycles).
/// </summary>
public sealed class SampleValueGenerator
{
    private readonly PreviewOptions _options;
    private readonly SerializationProfile _profile;
    private readonly List<PreviewDiagnostic> _diagnostics = new();
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly List<ObjectContract> _path = new();
    private int _nodes;
    private bool _incomplete;

    private SampleValueGenerator(PreviewOptions options, SerializationProfile profile)
    {
        _options = options.Normalize();
        _profile = profile;
    }

    public static SampleGenerationResult Generate(ResolvedContract contract, PreviewOptions options)
    {
        var generator = new SampleValueGenerator(options, contract.Profile);
        var root = generator.GenerateRoot(contract.Root);
        return new SampleGenerationResult(root, generator._diagnostics, generator._incomplete);
    }

    private bool ClrDefaults => _options.Mode == SampleMode.ClrDefaults;

    private SampleValue GenerateRoot(JsonContract root)
    {
        if (root is ObjectContract obj && obj.Polymorphism is { } poly && _options.PolymorphicVariant is { } variantKey)
        {
            var variant = poly.Variants.FirstOrDefault(v => v.VariantKey == variantKey);
            if (variant?.Contract is { } variantContract)
            {
                var value = GenerateObject(variantContract, "$", 0, null);
                if (value is ObjectSampleValue o)
                {
                    o.Variant = variant;
                }

                return value;
            }

            Report(DiagnosticIds.PolymorphicVariantUnknown, "$", null, variantKey, obj.Type.DisplayName);
        }

        // The root value is always materialized (CLR-defaults mode shows the default instance, not null).
        return Generate(root, "$", 0, memberName: null, declaringType: null, variation: 0, isRoot: true);
    }

    private SampleValue Generate(JsonContract contract, string path, int depth, string? memberName, string? declaringType, int variation, bool isRoot = false)
    {
        if (!TryCountNode(path))
        {
            return NullSampleValue.Instance;
        }

        switch (contract)
        {
            case PrimitiveContract primitive:
                if (ClrDefaults)
                {
                    return SampleHeuristics.IsReferenceKind(primitive.Primitive)
                        ? NullSampleValue.Instance
                        : SampleHeuristics.Default(primitive.Primitive);
                }

                return SampleHeuristics.Sample(primitive.Primitive, memberName, declaringType, variation, _options.UseNameHeuristics);

            case EnumContract enumContract:
                return new EnumSampleValue(enumContract, ClrDefaults ? 0UL : PickEnumValue(enumContract, variation));

            case ObjectContract obj:
                if (ClrDefaults && !obj.Type.IsValueType && !isRoot)
                {
                    return NullSampleValue.Instance;
                }

                return GenerateObject(obj, path, depth, null);

            case CollectionContract collection:
                return ClrDefaults && !isRoot ? NullSampleValue.Instance : GenerateCollection(collection, path, depth);

            case DictionaryContract dictionary:
                return ClrDefaults && !isRoot ? NullSampleValue.Instance : GenerateDictionary(dictionary, path, depth);

            case AnyContract any:
                return any.AnyKind == AnyKind.Unbound || (ClrDefaults && !isRoot) ? NullSampleValue.Instance : new AnySampleValue(any);

            default:
                return NullSampleValue.Instance;
        }
    }

    private SampleValue GenerateObject(ObjectContract contract, string path, int depth, PolymorphicVariant? variant)
    {
        var isValueType = contract.Type.IsValueType;
        if (depth >= _options.MaxDepth)
        {
            _incomplete = true;
            Report(DiagnosticIds.DepthLimit, path, null, _options.MaxDepth);
            return isValueType ? new ObjectSampleValue(contract, true) { Truncated = true } : NullSampleValue.Instance;
        }

        if (!isValueType && _path.Contains(contract))
        {
            Report(DiagnosticIds.PotentialCycle, path, null, contract.Type.DisplayName, CycleNote());
            return NullSampleValue.Instance;
        }

        if (contract.Polymorphism is { Variants.Count: > 0 } poly && path != "$")
        {
            Report(DiagnosticIds.PolymorphicRuntimeType, path, null, contract.Type.DisplayName, string.Join(", ", poly.Variants.Select(v => v.Type.DisplayName)));
        }

        var value = new ObjectSampleValue(contract, isValueType) { Variant = variant };
        _path.Add(contract);
        try
        {
            foreach (var member in contract.Members)
            {
                if (!member.Included || member.IsExtensionData || member.Contract is null)
                {
                    continue;
                }

                if (_nodes >= _options.MaxNodes)
                {
                    value.Truncated = true;
                    TryCountNode(path);
                    break;
                }

                var memberPath = path + "." + member.JsonName;
                SampleValue memberValue;
                if (ClrDefaults)
                {
                    memberValue = member.CanBeNull ? NullSampleValue.Instance : Generate(member.Contract, memberPath, depth + 1, member.ClrName, contract.Type.DisplayName, 0);
                }
                else if (_options.NullableMembersAsNull && (member.IsNullableAnnotated || member.IsNullableValueType))
                {
                    memberValue = NullSampleValue.Instance;
                }
                else
                {
                    memberValue = Generate(member.Contract, memberPath, depth + 1, member.ClrName, contract.Type.DisplayName, 0);
                }

                value.Members.Add(new KeyValuePair<PropertyContract, SampleValue>(member, memberValue));
            }
        }
        finally
        {
            _path.RemoveAt(_path.Count - 1);
        }

        return value;
    }

    private SampleValue GenerateCollection(CollectionContract contract, string path, int depth)
    {
        var value = new CollectionSampleValue(contract);
        if (depth >= _options.MaxDepth)
        {
            _incomplete = true;
            value.Truncated = true;
            Report(DiagnosticIds.DepthLimit, path, null, _options.MaxDepth);
            return value;
        }

        if (contract.Element is ObjectContract { Type.IsValueType: false } element && _path.Contains(element))
        {
            Report(DiagnosticIds.PotentialCycle, path, null, element.Type.DisplayName, "Recursive elements are shown as an empty collection. " + CycleNote());
            return value;
        }

        var count = ClrDefaults ? 0 : _options.CollectionCount;
        if (contract.RequiresUniqueElements)
        {
            count = Math.Min(count, MaxDistinct(contract.Element));
        }

        if (contract.Rank > 1)
        {
            // Nested arrays: each dimension uses the configured count (minimum 1 when count > 0).
            var dims = Enumerable.Repeat(count, contract.Rank).ToArray();
            value.Dimensions = dims;
            var total = dims.Aggregate(1, (a, b) => a * b);
            for (var i = 0; i < total; i++)
            {
                if (_nodes >= _options.MaxNodes)
                {
                    value.Truncated = true;
                    TryCountNode(path);
                    break;
                }

                value.Items.Add(Generate(contract.Element, path + "[" + i + "]", depth + 1, null, null, i));
            }

            return value;
        }

        for (var i = 0; i < count; i++)
        {
            if (_nodes >= _options.MaxNodes)
            {
                value.Truncated = true;
                TryCountNode(path);
                break;
            }

            value.Items.Add(Generate(contract.Element, path + "[" + i + "]", depth + 1, null, null, i));
        }

        return value;
    }

    private SampleValue GenerateDictionary(DictionaryContract contract, string path, int depth)
    {
        var value = new DictionarySampleValue(contract);
        if (contract.Classification == Classification.Unsupported || ClrDefaults)
        {
            return value;
        }

        if (depth >= _options.MaxDepth)
        {
            _incomplete = true;
            Report(DiagnosticIds.DepthLimit, path, null, _options.MaxDepth);
            return value;
        }

        if (contract.Value is ObjectContract { Type.IsValueType: false } element && _path.Contains(element))
        {
            Report(DiagnosticIds.PotentialCycle, path, null, element.Type.DisplayName, "Recursive entries are shown as an empty object. " + CycleNote());
            return value;
        }

        var count = Math.Min(_options.CollectionCount, MaxDistinct(contract.Key));
        for (var i = 0; i < count; i++)
        {
            if (_nodes >= _options.MaxNodes)
            {
                TryCountNode(path);
                break;
            }

            SampleValue key = contract.Key switch
            {
                PrimitiveContract { Primitive: PrimitiveKind.String } => new ScalarSampleValue(PrimitiveKind.String, "key" + (i + 1), false),
                EnumContract e => new EnumSampleValue(e, PickEnumValue(e, i)),
                PrimitiveContract p => SampleHeuristics.Sample(p.Primitive, null, null, i, false),
                _ => new ScalarSampleValue(PrimitiveKind.String, "key" + (i + 1), false),
            };
            var keyText = "key" + (i + 1);
            value.Entries.Add(new KeyValuePair<SampleValue, SampleValue>(key, Generate(contract.Value, path + "." + keyText, depth + 1, null, null, i)));
        }

        return value;
    }

    private static int MaxDistinct(JsonContract contract) => contract switch
    {
        PrimitiveContract { Primitive: PrimitiveKind.Boolean } => 2,
        EnumContract e => Math.Max(1, e.Members.Select(m => m.RawValue).Distinct().Count()),
        _ => int.MaxValue,
    };

    private static ulong PickEnumValue(EnumContract contract, int variation)
    {
        var distinct = contract.Members.GroupBy(m => m.RawValue).Select(g => g.First()).ToList();
        if (distinct.Count == 0)
        {
            return 0;
        }

        // Prefer non-zero members (a zero member is usually "None"/"Unknown"); zero members come last so that
        // consecutive variations stay unique up to the number of distinct members.
        var ordered = distinct.Where(m => m.RawValue != 0).Concat(distinct.Where(m => m.RawValue == 0)).ToList();
        return ordered[variation % ordered.Count].RawValue;
    }

    private string CycleNote() => _profile.ReferenceHandling switch
    {
        ReferenceHandlingMode.Preserve => "If the runtime graph is cyclic, the serializer writes a {\"$ref\": …} reference; object identity is not known statically.",
        ReferenceHandlingMode.IgnoreCycles => _profile.Serializer == SerializerKind.SystemTextJson
            ? "If the runtime graph is cyclic, System.Text.Json writes null at the cycle (IgnoreCycles)."
            : "If the runtime graph is cyclic, Newtonsoft.Json skips the looping member (ReferenceLoopHandling.Ignore).",
        _ => "Nested values are truncated. If the runtime graph is cyclic, the serializer throws (no reference handling configured).",
    };

    private bool TryCountNode(string path)
    {
        if (_nodes >= _options.MaxNodes)
        {
            _incomplete = true;
            Report(DiagnosticIds.NodeLimit, path, null, _options.MaxNodes);
            return false;
        }

        _nodes++;
        return true;
    }

    private void Report(string id, string? path, string? member, params object[] args)
    {
        var d = DiagnosticCatalog.Create(id, path, member, args);
        var key = id == DiagnosticIds.PotentialCycle || id == DiagnosticIds.PolymorphicRuntimeType ? id + d.Message : id;
        if (_reported.Add(key))
        {
            _diagnostics.Add(d);
        }
    }
}
