using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using RealizeJSON.Core;

namespace RealizeJSON.CodeAnalysis.Tests;

public sealed class ConfigurationDetectorTests
{
    private static DetectedConfiguration[] Scan(string source) =>
        JsonConfigurationDetector.Scan(CSharpSyntaxTree.ParseText(source).GetRoot(), "Program.cs").ToArray();

    [Fact]
    public void Detects_AddJsonOptions_settings()
    {
        var result = Scan("""
            builder.Services.AddControllers().AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
                o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
                o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
                o.JsonSerializerOptions.IncludeFields = true;
            });
            """).Single();

        Assert.Equal(SerializerKind.SystemTextJson, result.Serializer);
        Assert.True(result.IsWebHost);
        Assert.Equal(NamingPolicyKind.SnakeCaseLower, result.Settings.PropertyNaming);
        Assert.Equal(DefaultIgnoreCondition.WhenWritingNull, result.Settings.IgnoreCondition);
        Assert.Equal(EnumRepresentation.Strings, result.Settings.Enums);
        Assert.Equal(NamingPolicyKind.CamelCase, result.Settings.EnumNaming);
        Assert.Equal(ReferenceHandlingMode.IgnoreCycles, result.Settings.ReferenceHandling);
        Assert.True(result.Settings.IncludeFields);
        Assert.Empty(result.UnknownSettings);
        Assert.Contains("AddJsonOptions", result.Source, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Detects_minimal_api_and_generic_configure()
    {
        var results = Scan("""
            builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = null);
            builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.WriteIndented = true);
            """);
        Assert.Equal(2, results.Length);
        Assert.Equal(NamingPolicyKind.Unchanged, results[0].Settings.PropertyNaming);
        Assert.True(results[1].Settings.WriteIndented);
    }

    [Fact]
    public void Non_literal_values_are_reported_as_unknown()
    {
        var result = Scan("""
            services.AddControllers().AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.PropertyNamingPolicy = new MyPolicy();
                o.JsonSerializerOptions.IncludeFields = config.GetValue<bool>("x");
                o.JsonSerializerOptions.Converters.Add(new MoneyConverter());
                o.JsonSerializerOptions.TypeInfoResolver = MyContext.Default;
            });
            """).Single();

        Assert.Null(result.Settings.PropertyNaming);
        Assert.Contains(result.UnknownSettings, u => u.StartsWith("PropertyNamingPolicy", System.StringComparison.Ordinal));
        Assert.Contains("IncludeFields", result.UnknownSettings);
        Assert.Contains("Converters.Add(MoneyConverter)", result.UnknownSettings);
        Assert.Contains(result.UnknownSettings, u => u.StartsWith("TypeInfoResolver", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Detects_newtonsoft_configuration()
    {
        var result = Scan("""
            services.AddControllers().AddNewtonsoftJson(o =>
            {
                o.SerializerSettings.ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy { ProcessDictionaryKeys = true } };
                o.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
                o.SerializerSettings.Converters.Add(new StringEnumConverter());
                o.SerializerSettings.TypeNameHandling = TypeNameHandling.Auto;
            });
            """).Single();

        Assert.Equal(SerializerKind.NewtonsoftJson, result.Serializer);
        Assert.Equal(NamingPolicyKind.SnakeCaseLower, result.Settings.PropertyNaming);
        Assert.Equal(NamingPolicyKind.SnakeCaseLower, result.Settings.DictionaryKeyNaming);
        Assert.Equal(DefaultIgnoreCondition.WhenWritingNull, result.Settings.IgnoreCondition);
        Assert.Equal(EnumRepresentation.Strings, result.Settings.Enums);
        Assert.Contains(result.UnknownSettings, u => u.StartsWith("TypeNameHandling", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Detects_camel_case_property_names_resolver_and_default_settings()
    {
        var result = Scan("""
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Formatting = Formatting.Indented,
            };
            """).Single();
        Assert.False(result.IsWebHost);
        Assert.Equal(NamingPolicyKind.CamelCase, result.Settings.PropertyNaming);
        Assert.Equal(NamingPolicyKind.CamelCase, result.Settings.DictionaryKeyNaming);
        Assert.True(result.Settings.OverrideSpecifiedNames);
        Assert.True(result.Settings.WriteIndented);
    }

    [Fact]
    public void Ignores_unrelated_code()
    {
        Assert.Empty(Scan("var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };"));
    }
}
