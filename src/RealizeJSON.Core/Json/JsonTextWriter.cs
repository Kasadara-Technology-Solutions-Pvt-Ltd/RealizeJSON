using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RealizeJSON.Core.Json;

/// <summary>Maps an output line to the JSON path (and CLR member) it renders.</summary>
public sealed record JsonLineMapping(int Line, string JsonPath, Contracts.PropertyContract? Member);

/// <summary>Result of writing a JSON DOM to text.</summary>
public sealed class JsonWriteResult
{
    public JsonWriteResult(string text, bool truncated, IReadOnlyList<JsonLineMapping> lineMap, IReadOnlyList<string> truncatedPaths)
    {
        Text = text;
        Truncated = truncated;
        LineMap = lineMap;
        TruncatedPaths = truncatedPaths;
    }

    public string Text { get; }

    public bool Truncated { get; }

    public IReadOnlyList<JsonLineMapping> LineMap { get; }

    public IReadOnlyList<string> TruncatedPaths { get; }
}

/// <summary>
/// Structured JSON writer. Output is always syntactically valid: when the size budget is exhausted, remaining
/// properties/items are skipped and open containers are closed.
/// </summary>
public static class JsonTextWriter
{
    private const int ClosingReserve = 64;

    public static JsonWriteResult Write(JsonNodeModel root, bool indented, JsonEscaping escaping, int maxChars = int.MaxValue)
    {
        var state = new WriterState(indented, escaping, maxChars);
        state.WriteValue(root, 0);
        return new JsonWriteResult(state.Builder.ToString(), state.Truncated, state.LineMap, state.TruncatedPaths);
    }

    /// <summary>Escapes a string value including the surrounding quotes.</summary>
    public static string Quote(string value, JsonEscaping escaping)
    {
        var sb = new StringBuilder(value.Length + 2);
        AppendQuoted(sb, value, escaping);
        return sb.ToString();
    }

    internal static void AppendQuoted(StringBuilder sb, string value, JsonEscaping escaping)
    {
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    sb.Append("\\\\");
                    continue;
                case '\n':
                    sb.Append("\\n");
                    continue;
                case '\r':
                    sb.Append("\\r");
                    continue;
                case '\t':
                    sb.Append("\\t");
                    continue;
                case '\b':
                    sb.Append("\\b");
                    continue;
                case '\f':
                    sb.Append("\\f");
                    continue;
            }

            if (escaping == JsonEscaping.NewtonsoftDefault)
            {
                if (c == '"')
                {
                    sb.Append("\\\"");
                }
                else if (c < 0x20 || c == '\u0085' || c == (char)0x2028 || c == (char)0x2029)
                {
                    sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                var allowed = c >= 0x20 && c <= 0x7E && c != '"' && c != '&' && c != '\'' && c != '+' && c != '<' && c != '>' && c != '`';
                if (allowed)
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                }
            }
        }

        sb.Append('"');
    }

    private sealed class WriterState
    {
        private readonly bool _indented;
        private readonly JsonEscaping _escaping;
        private readonly int _maxChars;
        private int _line;

        public WriterState(bool indented, JsonEscaping escaping, int maxChars)
        {
            _indented = indented;
            _escaping = escaping;
            _maxChars = maxChars;
        }

        public StringBuilder Builder { get; } = new();

        public bool Truncated { get; private set; }

        public List<JsonLineMapping> LineMap { get; } = new();

        public List<string> TruncatedPaths { get; } = new();

        private bool OverBudget => Builder.Length + ClosingReserve > _maxChars;

        public void WriteValue(JsonNodeModel node, int depth)
        {
            switch (node)
            {
                case JsonObjectNode obj:
                    WriteObject(obj, depth);
                    break;
                case JsonArrayNode array:
                    WriteArray(array, depth);
                    break;
                case JsonStringNode s when !s.Escape && IsSafeAscii(s.Value):
                    Builder.Append('"').Append(s.Value).Append('"');
                    break;
                case JsonStringNode s:
                    AppendQuoted(Builder, s.Value, _escaping);
                    break;
                case JsonNumberNode n:
                    Builder.Append(n.Text);
                    break;
                case JsonBooleanNode b:
                    Builder.Append(b.Value ? "true" : "false");
                    break;
                default:
                    Builder.Append("null");
                    break;
            }
        }

        private static bool IsSafeAscii(string value)
        {
            foreach (var c in value)
            {
                if (c < 0x20 || c > 0x7E || c == '"' || c == '\\')
                {
                    return false;
                }
            }

            return true;
        }

        private void WriteObject(JsonObjectNode obj, int depth)
        {
            if (obj.Properties.Count == 0)
            {
                Builder.Append("{}");
                return;
            }

            Builder.Append('{');
            var first = true;
            foreach (var property in obj.Properties)
            {
                if (OverBudget)
                {
                    MarkTruncated(obj.Path);
                    break;
                }

                if (!first)
                {
                    Builder.Append(',');
                }

                first = false;
                NewLine(depth + 1);
                LineMap.Add(new JsonLineMapping(_line, property.Value.Path, property.Member));
                AppendQuoted(Builder, property.Name, _escaping);
                Builder.Append(_indented ? ": " : ":");
                WriteValue(property.Value, depth + 1);
            }

            NewLine(depth);
            Builder.Append('}');
        }

        private void WriteArray(JsonArrayNode array, int depth)
        {
            if (array.Items.Count == 0)
            {
                Builder.Append("[]");
                return;
            }

            Builder.Append('[');
            var first = true;
            foreach (var item in array.Items)
            {
                if (OverBudget)
                {
                    MarkTruncated(array.Path);
                    break;
                }

                if (!first)
                {
                    Builder.Append(',');
                }

                first = false;
                NewLine(depth + 1);
                LineMap.Add(new JsonLineMapping(_line, item.Path, null));
                WriteValue(item, depth + 1);
            }

            NewLine(depth);
            Builder.Append(']');
        }

        private void MarkTruncated(string path)
        {
            Truncated = true;
            if (TruncatedPaths.Count < 20)
            {
                TruncatedPaths.Add(path);
            }
        }

        private void NewLine(int depth)
        {
            if (!_indented)
            {
                return;
            }

            Builder.Append('\n');
            _line++;
            Builder.Append(' ', depth * 2);
        }
    }
}
