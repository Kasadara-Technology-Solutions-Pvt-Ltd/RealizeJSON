using Microsoft.CodeAnalysis;
using RealizeJSON.Tests.Shared;

namespace RealizeJSON.CodeAnalysis.Tests;

public sealed class TypeKeyTests
{
    private const string Source = """
        using System.Collections.Generic;
        namespace Ns
        {
            public class Outer<T> { public class Inner { public T Value { get; set; } = default!; } }
            public class Dto { public int Id { get; set; } }
            public class Holder { public Outer<Dto>.Inner X { get; set; } = null!; public Dictionary<string, List<Dto>>[] Y { get; set; } = null!; }
        }
        """;

    [Fact]
    public void Keys_round_trip_across_compilations()
    {
        var first = TestCompilation.Create(Source);
        var holder = first.GetTypeByMetadataName("Ns.Holder")!;
        foreach (var property in new[] { "X", "Y" })
        {
            var type = ((IPropertySymbol)holder.GetMembers(property)[0]).Type;
            var key = TypeKey.Create(type)!;

            // A new compilation (e.g. after an edit) must resolve the same type without retaining the old one.
            var second = TestCompilation.Create(Source + "\n// edited");
            var resolved = key.Resolve(second);
            Assert.NotNull(resolved);
            Assert.Equal(type.ToDisplayString(), resolved!.ToDisplayString());
            Assert.Equal(key, TypeKey.Create(resolved));
            Assert.Equal(key, TypeKey.Parse(key.ToString()));
            Assert.Equal(type.ToDisplayString(), TypeKey.Parse(key.ToString())!.Resolve(second)!.ToDisplayString());
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("A[[B]")]
    [InlineData("#x:A")]
    [InlineData("A[[B]]extra[")]
    public void Malformed_keys_do_not_parse(string text) => Assert.Null(TypeKey.Parse(text));

    [Fact]
    public void Missing_types_resolve_to_null()
    {
        var key = TypeKey.Create(TestCompilation.Create(Source).GetTypeByMetadataName("Ns.Dto")!)!;
        Assert.Null(key.Resolve(TestCompilation.Create("namespace Ns { }")));
    }
}
