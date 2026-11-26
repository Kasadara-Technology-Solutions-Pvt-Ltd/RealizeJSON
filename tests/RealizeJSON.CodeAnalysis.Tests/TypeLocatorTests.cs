using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using RealizeJSON.Tests.Shared;

namespace RealizeJSON.CodeAnalysis.Tests;

public sealed class TypeLocatorTests
{
    private const string Source = """
        using System.Collections.Generic;
        using Alias = Shop.Address;

        namespace Shop
        {
            public partial class Customer
            {
                public int Id { get; set; }
                public Address Home { get; set; } = new();
                public List<Address> Others { get; set; } = new();
                public Alias Billing { get; set; } = new();
                public string Name { get; set; } = "";

                public class Nested { public int Value { get; set; } }
            }

            public partial class Customer
            {
                public Wrapper<Order> Wrapped { get; set; } = new();
            }

            public class Address { public string City { get; set; } = ""; }
            public class Order { public int Number { get; set; } }
            public class Wrapper<T> { public T Item { get; set; } = default!; }
            public enum Status { Active }
            public struct Point { public int X { get; set; } }
            public record Person(string Name);
            public static class Helpers { public static void M() { var c = new Customer(); } }
        }
        """;

    private static TypeLocation Locate(string marker, int offset = 0)
    {
        var compilation = TestCompilation.Create(Source);
        var tree = compilation.SyntaxTrees.Single();
        var position = tree.ToString().IndexOf(marker, System.StringComparison.Ordinal) + offset;
        Assert.True(position >= offset, "marker not found: " + marker);
        return TypeLocator.Locate(compilation.GetSemanticModel(tree), tree.GetRoot(), position, CancellationToken.None);
    }

    [Theory]
    [InlineData("Customer\n", 2, "Shop.Customer", CandidateReason.Declaration)]
    [InlineData("Address { public string", 1, "Shop.Address", CandidateReason.Declaration)]
    [InlineData("Status {", 0, "Shop.Status", CandidateReason.Declaration)]
    [InlineData("Point {", 0, "Shop.Point", CandidateReason.Declaration)]
    [InlineData("Person(", 0, "Shop.Person", CandidateReason.Declaration)]
    [InlineData("Nested {", 0, "Shop.Customer.Nested", CandidateReason.Declaration)]
    public void Declarations_are_selected(string marker, int offset, string expected, CandidateReason reason)
    {
        var location = Locate(marker, offset);
        Assert.Equal(expected, location.Primary!.Symbol.ToDisplayString());
        Assert.Equal(reason, location.Primary.Reason);
    }

    [Fact]
    public void Type_reference_under_caret_wins_over_enclosing_type()
    {
        var location = Locate("Address Home", 2);
        Assert.Equal("Shop.Address", location.Primary!.Symbol.ToDisplayString());
        Assert.Equal(CandidateReason.Reference, location.Primary.Reason);
        Assert.Contains(location.Candidates, c => c.Symbol.ToDisplayString() == "Shop.Customer");
    }

    [Fact]
    public void Aliases_resolve_to_the_target_type()
    {
        var location = Locate("Alias Billing", 1);
        Assert.Equal("Shop.Address", location.Primary!.Symbol.ToDisplayString());
    }

    [Fact]
    public void Generic_argument_reference_is_selected_and_framework_collection_is_secondary()
    {
        var onArgument = Locate("List<Address>", 6);
        Assert.Equal("Shop.Address", onArgument.Primary!.Symbol.ToDisplayString());

        var onList = Locate("List<Address>", 1);
        Assert.Equal("Shop.Customer", onList.Primary!.Symbol.ToDisplayString());
        Assert.Contains(onList.Candidates, c => c.Symbol.ToDisplayString() == "System.Collections.Generic.List<Shop.Address>");
    }

    [Fact]
    public void Constructed_generic_reference_is_selected()
    {
        var location = Locate("Wrapper<Order> Wrapped", 1);
        Assert.Equal("Shop.Wrapper<Shop.Order>", location.Primary!.Symbol.ToDisplayString());
    }

    [Fact]
    public void Predefined_types_do_not_steal_the_selection()
    {
        var location = Locate("string Name", 2);
        Assert.Equal("Shop.Customer", location.Primary!.Symbol.ToDisplayString());
    }

    [Fact]
    public void Partial_declarations_resolve_to_one_symbol()
    {
        var location = Locate("Wrapped { get", 0);
        Assert.Equal("Shop.Customer", location.Primary!.Symbol.ToDisplayString());
        Assert.Equal(2, ((INamedTypeSymbol)location.Primary.Symbol).DeclaringSyntaxReferences.Length);
    }

    [Fact]
    public void Open_generic_declaration_offers_constructed_usages()
    {
        var location = Locate("Wrapper<T>", 1);
        Assert.Equal("Shop.Wrapper<T>", location.Primary!.Symbol.ToDisplayString());
        Assert.Contains(location.Candidates, c => c.Reason == CandidateReason.ConstructedUsage && c.Symbol.ToDisplayString() == "Shop.Wrapper<Shop.Order>");
    }

    [Fact]
    public void Var_resolves_to_inferred_type_in_static_class()
    {
        var location = Locate("var c", 1);
        Assert.Equal("Shop.Customer", location.Primary!.Symbol.ToDisplayString());
    }

    [Fact]
    public void No_type_outside_declarations()
    {
        var location = Locate("using System", 0);
        Assert.Null(location.Primary);
    }

    [Fact]
    public void Enclosing_declaration_span_is_reported_for_relevance_checks()
    {
        var location = Locate("public int Id", 4);
        Assert.NotNull(location.EnclosingDeclarationSpan);
        Assert.Equal("Shop.Customer", location.Primary!.Symbol.ToDisplayString());
    }
}
