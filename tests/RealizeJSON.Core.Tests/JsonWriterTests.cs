using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using RealizeJSON.Core.Json;
using N = Newtonsoft.Json;

namespace RealizeJSON.Core.Tests;

public sealed class JsonWriterTests
{
    public static IEnumerable<TheoryDataRow<string>> Strings() => new[]
    {
        "plain", "quote\"inside", "back\\slash", "tab\tnew\nline\rcr", "html <tag> & 'apos' +plus `tick`",
        "unicode é ü 中文 😀", "\u0001\u001f control", "\u007f del", "line\u2028sep\u2029para\u0085nel", "",
    }.Select(s => new TheoryDataRow<string>(s));

    [Theory]
    [MemberData(nameof(Strings))]
    public void Escaping_matches_System_Text_Json(string value)
    {
        Assert.Equal(JsonSerializer.Serialize(value), JsonTextWriter.Quote(value, JsonEscaping.SystemTextJsonDefault));
    }

    [Theory]
    [MemberData(nameof(Strings))]
    public void Escaping_matches_Newtonsoft(string value)
    {
        Assert.Equal(N.JsonConvert.SerializeObject(value), JsonTextWriter.Quote(value, JsonEscaping.NewtonsoftDefault));
    }

    [Fact]
    public void Indented_output_matches_serializer_layout_and_maps_lines()
    {
        var root = new JsonObjectNode();
        root.Properties.Add(new JsonPropertyNode("a", new JsonNumberNode("1") { Path = "$.a" }, null));
        var array = new JsonArrayNode { Path = "$.b" };
        array.Items.Add(new JsonBooleanNode(true) { Path = "$.b[0]" });
        root.Properties.Add(new JsonPropertyNode("b", array, null));
        root.Properties.Add(new JsonPropertyNode("c", new JsonObjectNode { Path = "$.c" }, null));
        root.Properties.Add(new JsonPropertyNode("d", new JsonArrayNode { Path = "$.d" }, null));

        var result = JsonTextWriter.Write(root, indented: true, JsonEscaping.SystemTextJsonDefault);
        var expected = JsonSerializer.Serialize(new { a = 1, b = new[] { true }, c = new { }, d = new int[0] }, new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" });
        Assert.Equal(expected, result.Text);
        Assert.Contains(result.LineMap, m => m.Line == 1 && m.JsonPath == "$.a");
        Assert.Contains(result.LineMap, m => m.Line == 3 && m.JsonPath == "$.b[0]");
    }

    [Fact]
    public void Budget_truncation_keeps_json_valid()
    {
        var root = new JsonArrayNode();
        for (var i = 0; i < 1000; i++)
        {
            var item = new JsonObjectNode { Path = $"$[{i}]" };
            item.Properties.Add(new JsonPropertyNode("value", new JsonStringNode(new string('x', 50)), null));
            root.Items.Add(item);
        }

        var result = JsonTextWriter.Write(root, indented: true, JsonEscaping.SystemTextJsonDefault, maxChars: 2048);
        Assert.True(result.Truncated);
        Assert.True(result.Text.Length <= 2048 + 64);
        using var document = JsonDocument.Parse(result.Text);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
    }

    [Theory]
    [InlineData("{\"a\":[1,2.5e3,-0.1,true,false,null,\"s\\u00e9\\n\"],\"b\":{}}")]
    [InlineData("[]")]
    [InlineData("  \"text\"  ")]
    [InlineData("{\"nested\":{\"deep\":[[[]]]}}")]
    public void Reader_round_trips_valid_json(string json)
    {
        var node = JsonTextReader.Parse(json);
        var written = JsonTextWriter.Write(node, indented: false, JsonEscaping.NewtonsoftDefault).Text;
        using var expected = JsonDocument.Parse(json);
        using var actual = JsonDocument.Parse(written);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"a\":}")]
    [InlineData("[1,]")]
    [InlineData("01")]
    [InlineData("\"unterminated")]
    [InlineData("{} trailing")]
    [InlineData("{'single':1}")]
    public void Reader_rejects_invalid_json(string json)
    {
        Assert.Throws<JsonReadException>(() => JsonTextReader.Parse(json));
    }

    [Fact]
    public void Reader_bounds_depth()
    {
        var deep = new string('[', 100) + new string(']', 100);
        Assert.Throws<JsonReadException>(() => JsonTextReader.Parse(deep));
    }
}
