using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using RealizeJSON.Core;
using RealizeJSON.Diagnostics;
using RealizeJSON.Tests.Shared;

namespace RealizeJSON.Serialization.Tests;

/// <summary>
/// The static preview must never run constructors, getters, static constructors or converters. Fixtures with
/// observable side effects are previewed from source and from metadata, then the side-effect counter is checked.
/// </summary>
public sealed class SecurityTests
{
    [Theory]
    [InlineData(SerializerKind.SystemTextJson)]
    [InlineData(SerializerKind.NewtonsoftJson)]
    public void Preview_does_not_execute_user_code(SerializerKind serializer)
    {
        // Metadata-only compilation: the fixture assembly is referenced as metadata, as for referenced projects/packages.
        var bytes = TestCompilation.Emit(Fixture.Compilation);
        var consumer = TestCompilation.Create("namespace Consumer { public class C { public Fixtures.Explosive? X { get; set; } } }", "Consumer",
            new[] { MetadataReference.CreateFromImage(bytes) });
        var fromMetadata = consumer.GetTypeByMetadataName("Fixtures.Explosive")!;
        Assert.True(fromMetadata.Locations.All(l => l.IsInMetadata));

        var context = new AssemblyLoadContext("security", isCollectible: true);
        try
        {
            // Loading the assembly does not run type initializers; the counter is read via reflection.
            var assembly = context.LoadFromStream(new MemoryStream(bytes));
            var counter = assembly.GetType("Fixtures.SideEffects")!.GetField("Count", BindingFlags.Public | BindingFlags.Static)!;

            var profile = ProfilePresets.Create(serializer, ProfilePreset.LibraryDefaults);
            var fromSource = PreviewHarness.Run(Fixture.Compilation, Fixture.Type("Explosive"), profile);
            var metadata = PreviewHarness.Run(consumer, fromMetadata, profile);

            Assert.Equal(0, (int)counter.GetValue(null)!);
            Assert.Contains("\"Value\"", fromSource.Json, System.StringComparison.Ordinal);
            Assert.Contains("\"Value\"", metadata.Json, System.StringComparison.Ordinal);
            if (serializer == SerializerKind.SystemTextJson)
            {
                Assert.True(fromSource.HasDiagnostic(DiagnosticIds.UnknownConverter));
            }

            Assert.Equal(Core.SourceOrigin.Metadata, metadata.Member("Value").Location.Origin);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void Sensitive_name_heuristic_flags_likely_secrets_only()
    {
        Assert.True(SensitiveNameHeuristic.IsPotentiallySensitive("ApiKey"));
        Assert.True(SensitiveNameHeuristic.IsPotentiallySensitive("Password"));
        Assert.True(SensitiveNameHeuristic.IsPotentiallySensitive("AccessToken"));
        Assert.False(SensitiveNameHeuristic.IsPotentiallySensitive("Shipping"));
        Assert.False(SensitiveNameHeuristic.IsPotentiallySensitive("UserName"));
    }
}
