using System.Collections.Generic;
using RealizeJSON.Core.Contracts;

namespace RealizeJSON.Core.Json;

/// <summary>Minimal immutable-by-convention JSON DOM used for preview emission.</summary>
public abstract class JsonNodeModel
{
    /// <summary>JSON path of this node, e.g. <c>$.orders[0].id</c>.</summary>
    public string Path { get; set; } = "$";
}

public sealed class JsonObjectNode : JsonNodeModel
{
    public IList<JsonPropertyNode> Properties { get; } = new List<JsonPropertyNode>();

    /// <summary>True when properties were omitted because a limit was reached.</summary>
    public bool Truncated { get; set; }
}

public sealed class JsonPropertyNode
{
    public JsonPropertyNode(string name, JsonNodeModel value, PropertyContract? member)
    {
        Name = name;
        Value = value;
        Member = member;
    }

    public string Name { get; }

    public JsonNodeModel Value { get; }

    /// <summary>CLR member that produced the property (null for metadata such as discriminators or dictionary entries).</summary>
    public PropertyContract? Member { get; }
}

public sealed class JsonArrayNode : JsonNodeModel
{
    public IList<JsonNodeModel> Items { get; } = new List<JsonNodeModel>();

    public bool Truncated { get; set; }
}

public sealed class JsonStringNode : JsonNodeModel
{
    public JsonStringNode(string value, bool escape = true)
    {
        Value = value;
        Escape = escape;
    }

    public string Value { get; }

    /// <summary>
    /// False when the serializer writes the value with a specialized writer that does not apply the string encoder
    /// (e.g. System.Text.Json dates, GUIDs and Base64). Such values only contain JSON-safe ASCII characters.
    /// </summary>
    public bool Escape { get; }
}

/// <summary>Number stored as already-formatted JSON number text (preserves decimal precision).</summary>
public sealed class JsonNumberNode : JsonNodeModel
{
    public JsonNumberNode(string text) => Text = text;

    public string Text { get; }
}

public sealed class JsonBooleanNode : JsonNodeModel
{
    public JsonBooleanNode(bool value) => Value = value;

    public bool Value { get; }
}

public sealed class JsonNullNode : JsonNodeModel
{
}
