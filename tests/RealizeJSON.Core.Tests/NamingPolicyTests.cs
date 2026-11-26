using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Newtonsoft.Json.Serialization;
using RealizeJSON.Core.Naming;

namespace RealizeJSON.Core.Tests;

/// <summary>Naming algorithms are differential-tested against the real serializers.</summary>
public sealed class NamingPolicyTests
{
    private static readonly string[] Names =
    {
        "Id", "URLValue", "IOStream", "myProp", "ABC", "X1Y2", "already_snake", "HTMLParser2Go", "Prop_Name", "AB1", "A",
        "a", "FOO bar", "Foo Bar", "Ünicode", "XMLHttpRequest", "iPhone", "Version2", "_private", "UPPER_CASE", "Ab_Cd", "",
        "PascalCase", "camelCase", "With-Dash", "Value123Abc", "ID", "IDs",
    };

    public static IEnumerable<TheoryDataRow<string>> AllNames() => Names.Select(n => new TheoryDataRow<string>(n));

    [Theory]
    [MemberData(nameof(AllNames))]
    public void System_Text_Json_policies_match(string name)
    {
        Assert.Equal(JsonNamingPolicy.CamelCase.ConvertName(name), NamingPolicies.Convert(NamingPolicyKind.CamelCase, name, SerializerKind.SystemTextJson));
        Assert.Equal(JsonNamingPolicy.SnakeCaseLower.ConvertName(name), NamingPolicies.Convert(NamingPolicyKind.SnakeCaseLower, name, SerializerKind.SystemTextJson));
        Assert.Equal(JsonNamingPolicy.SnakeCaseUpper.ConvertName(name), NamingPolicies.Convert(NamingPolicyKind.SnakeCaseUpper, name, SerializerKind.SystemTextJson));
        Assert.Equal(JsonNamingPolicy.KebabCaseLower.ConvertName(name), NamingPolicies.Convert(NamingPolicyKind.KebabCaseLower, name, SerializerKind.SystemTextJson));
        Assert.Equal(JsonNamingPolicy.KebabCaseUpper.ConvertName(name), NamingPolicies.Convert(NamingPolicyKind.KebabCaseUpper, name, SerializerKind.SystemTextJson));
        Assert.Equal(name, NamingPolicies.Convert(NamingPolicyKind.Unchanged, name, SerializerKind.SystemTextJson));
    }

    [Theory]
    [MemberData(nameof(AllNames))]
    public void Newtonsoft_strategies_match(string name)
    {
        Assert.Equal(new CamelCaseNamingStrategy().GetPropertyName(name, false), NamingPolicies.Convert(NamingPolicyKind.CamelCase, name, SerializerKind.NewtonsoftJson));
        Assert.Equal(new SnakeCaseNamingStrategy().GetPropertyName(name, false), NamingPolicies.Convert(NamingPolicyKind.SnakeCaseLower, name, SerializerKind.NewtonsoftJson));
        Assert.Equal(new KebabCaseNamingStrategy().GetPropertyName(name, false), NamingPolicies.Convert(NamingPolicyKind.KebabCaseLower, name, SerializerKind.NewtonsoftJson));
    }

    [Fact]
    public void Newtonsoft_has_no_upper_case_strategies()
    {
        Assert.False(NamingPolicies.IsSupported(NamingPolicyKind.SnakeCaseUpper, SerializerKind.NewtonsoftJson));
        Assert.True(NamingPolicies.IsSupported(NamingPolicyKind.SnakeCaseUpper, SerializerKind.SystemTextJson));
    }
}
