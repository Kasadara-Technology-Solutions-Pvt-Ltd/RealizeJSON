using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using RealizeJSON.Core;
using RealizeJSON.Core.Contracts;
using RealizeJSON.Core.Json;
using RealizeJSON.SampleData;
using RealizeJSON.Serialization;
using RealizeJSON.Serialization.NewtonsoftJson;
using RealizeJSON.Serialization.SystemTextJson;

namespace RealizeJSON.Tests.Shared;

/// <summary>Result of a static preview in tests.</summary>
public sealed record HarnessResult(string Json, ResolvedContract Contract, SampleGenerationResult Samples, JsonWriteResult Written)
{
    public System.Collections.Generic.IEnumerable<PreviewDiagnostic> AllDiagnostics => Contract.Diagnostics.Concat(Samples.Diagnostics);

    public bool HasDiagnostic(string id) => AllDiagnostics.Any(d => d.Id == id);

    public ObjectContract RootObject => (ObjectContract)Contract.Root;

    public PropertyContract Member(string clrName) => RootObject.Members.First(m => m.ClrName == clrName);
}

/// <summary>Runs the static pipeline (no workspace) against a compilation.</summary>
public static class PreviewHarness
{
    public static IRuleProvider Provider(SerializerKind serializer) => serializer == SerializerKind.SystemTextJson
        ? new SystemTextJsonRuleProvider()
        : new NewtonsoftJsonRuleProvider();

    public static HarnessResult Run(Compilation compilation, ITypeSymbol type, SerializationProfile profile, PreviewOptions? options = null)
    {
        var contract = Provider(profile.Serializer).BuildContract(type, compilation, profile, CancellationToken.None);
        var samples = SampleValueGenerator.Generate(contract, options ?? new PreviewOptions());
        var tree = JsonEmitter.Emit(contract, samples.Root);
        var written = JsonTextWriter.Write(tree, profile.WriteIndented, contract.Formatter.Escaping, (options ?? new PreviewOptions()).Normalize().MaxBytes);
        return new HarnessResult(written.Text, contract, samples, written);
    }

    public static HarnessResult Run(string source, string typeName, SerializationProfile profile, PreviewOptions? options = null)
    {
        var compilation = TestCompilation.Create(source);
        return Run(compilation, TestCompilation.GetType(compilation, typeName), profile, options);
    }

    public static SerializationProfile Stj(ProfilePreset preset = ProfilePreset.LibraryDefaults, bool indented = false) =>
        ProfilePresets.Create(SerializerKind.SystemTextJson, preset) with { WriteIndented = indented };

    public static SerializationProfile Newtonsoft(ProfilePreset preset = ProfilePreset.LibraryDefaults, bool indented = false) =>
        ProfilePresets.Create(SerializerKind.NewtonsoftJson, preset) with { WriteIndented = indented };
}
