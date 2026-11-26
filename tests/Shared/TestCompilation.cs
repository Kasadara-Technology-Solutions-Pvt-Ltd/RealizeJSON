using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RealizeJSON.Tests.Shared;

/// <summary>Builds Roslyn compilations for fixtures using the test host's reference assemblies.</summary>
public static class TestCompilation
{
    private static readonly Lazy<IReadOnlyList<MetadataReference>> References = new(LoadReferences);

    public static IReadOnlyList<MetadataReference> DefaultReferences => References.Value;

    public static CSharpParseOptions ParseOptions { get; } = new(LanguageVersion.CSharp12);

    public static CSharpCompilation Create(string source, string assemblyName = "Fixture", IEnumerable<MetadataReference>? extra = null, bool allowErrors = false)
    {
        var tree = CSharpSyntaxTree.ParseText(source, ParseOptions, path: "Fixture.cs");
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { tree },
            DefaultReferences.Concat(extra ?? Array.Empty<MetadataReference>()),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        if (!allowErrors)
        {
            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            if (errors.Count > 0)
            {
                throw new InvalidOperationException("Fixture does not compile:\n" + string.Join("\n", errors));
            }
        }

        return compilation;
    }

    public static INamedTypeSymbol GetType(Compilation compilation, string metadataName) =>
        compilation.GetTypeByMetadataName(metadataName) ?? throw new InvalidOperationException("Type not found: " + metadataName);

    /// <summary>Emits a compilation to bytes (tests only; the extension never emits or loads user code).</summary>
    public static byte[] Emit(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException("Emit failed:\n" + string.Join("\n", result.Diagnostics));
        }

        return stream.ToArray();
    }

    private static IReadOnlyList<MetadataReference> LoadReferences()
    {
        var tpa = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var list = tpa
            .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(p) is "mscorlib.dll" or "netstandard.dll" or "Newtonsoft.Json.dll")
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
        var newtonsoft = typeof(Newtonsoft.Json.JsonConvert).Assembly.Location;
        if (!tpa.Any(p => p.EndsWith("Newtonsoft.Json.dll", StringComparison.OrdinalIgnoreCase)))
        {
            list.Add(MetadataReference.CreateFromFile(newtonsoft));
        }

        return list;
    }
}
