using System;
using System.Globalization;
using System.Text;

namespace RealizeJSON.Core.Json;

/// <summary>Thrown for malformed JSON input.</summary>
public sealed class JsonReadException : Exception
{
    public JsonReadException(string message, int position)
        : base($"{message} (position {position})") => Position = position;

    public JsonReadException()
    {
    }

    public JsonReadException(string message)
        : base(message)
    {
    }

    public JsonReadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public int Position { get; }
}

/// <summary>
/// Strict RFC 8259 JSON parser for small configuration documents (settings, solution profiles).
/// Not used on untrusted large inputs; depth and length are bounded.
/// </summary>
public static class JsonTextReader
{
    public const int MaxDepth = 64;
    public const int MaxLength = 1024 * 1024;

    public static JsonNodeModel Parse(string text)
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        if (text.Length > MaxLength)
        {
            throw new JsonReadException("Document too large", 0);
        }

        var parser = new Parser(text);
        parser.SkipWhitespace();
        var value = parser.ParseValue(0, "$");
        parser.SkipWhitespace();
        if (!parser.AtEnd)
        {
            throw new JsonReadException("Unexpected trailing content", parser.Position);
        }

        return value;
    }

    private sealed class Parser
    {
        private readonly string _text;

        public Parser(string text) => _text = text;

        public int Position { get; private set; }

        public bool AtEnd => Position >= _text.Length;

        public void SkipWhitespace()
        {
            while (!AtEnd && (_text[Position] == ' ' || _text[Position] == '\t' || _text[Position] == '\n' || _text[Position] == '\r'))
            {
                Position++;
            }
        }

        public JsonNodeModel ParseValue(int depth, string path)
        {
            if (depth > MaxDepth)
            {
                throw new JsonReadException("Maximum depth exceeded", Position);
            }

            if (AtEnd)
            {
                throw new JsonReadException("Unexpected end of input", Position);
            }

            JsonNodeModel node;
            switch (_text[Position])
            {
                case '{':
                    node = ParseObject(depth, path);
                    break;
                case '[':
                    node = ParseArray(depth, path);
                    break;
                case '"':
                    node = new JsonStringNode(ParseString());
                    break;
                case 't':
                    Expect("true");
                    node = new JsonBooleanNode(true);
                    break;
                case 'f':
                    Expect("false");
                    node = new JsonBooleanNode(false);
                    break;
                case 'n':
                    Expect("null");
                    node = new JsonNullNode();
                    break;
                default:
                    node = new JsonNumberNode(ParseNumber());
                    break;
            }

            node.Path = path;
            return node;
        }

        private JsonObjectNode ParseObject(int depth, string path)
        {
            var obj = new JsonObjectNode();
            Position++;
            SkipWhitespace();
            if (!AtEnd && _text[Position] == '}')
            {
                Position++;
                return obj;
            }

            while (true)
            {
                SkipWhitespace();
                if (AtEnd || _text[Position] != '"')
                {
                    throw new JsonReadException("Expected property name", Position);
                }

                var name = ParseString();
                SkipWhitespace();
                Require(':');
                SkipWhitespace();
                var value = ParseValue(depth + 1, path + "." + name);
                obj.Properties.Add(new JsonPropertyNode(name, value, null));
                SkipWhitespace();
                if (AtEnd)
                {
                    throw new JsonReadException("Unterminated object", Position);
                }

                if (_text[Position] == ',')
                {
                    Position++;
                    continue;
                }

                Require('}');
                return obj;
            }
        }

        private JsonArrayNode ParseArray(int depth, string path)
        {
            var array = new JsonArrayNode();
            Position++;
            SkipWhitespace();
            if (!AtEnd && _text[Position] == ']')
            {
                Position++;
                return array;
            }

            while (true)
            {
                SkipWhitespace();
                array.Items.Add(ParseValue(depth + 1, path + "[" + array.Items.Count.ToString(CultureInfo.InvariantCulture) + "]"));
                SkipWhitespace();
                if (AtEnd)
                {
                    throw new JsonReadException("Unterminated array", Position);
                }

                if (_text[Position] == ',')
                {
                    Position++;
                    continue;
                }

                Require(']');
                return array;
            }
        }

        private string ParseString()
        {
            Position++;
            var sb = new StringBuilder();
            while (true)
            {
                if (AtEnd)
                {
                    throw new JsonReadException("Unterminated string", Position);
                }

                var c = _text[Position++];
                if (c == '"')
                {
                    return sb.ToString();
                }

                if (c < 0x20)
                {
                    throw new JsonReadException("Control character in string", Position - 1);
                }

                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (AtEnd)
                {
                    throw new JsonReadException("Unterminated escape", Position);
                }

                var e = _text[Position++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (Position + 4 > _text.Length ||
                            !int.TryParse(_text.Substring(Position, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                        {
                            throw new JsonReadException("Invalid unicode escape", Position);
                        }

                        sb.Append((char)code);
                        Position += 4;
                        break;
                    default:
                        throw new JsonReadException("Invalid escape", Position - 1);
                }
            }
        }

        private string ParseNumber()
        {
            var start = Position;
            if (!AtEnd && _text[Position] == '-')
            {
                Position++;
            }

            if (AtEnd || !char.IsDigit(_text[Position]))
            {
                throw new JsonReadException("Invalid value", start);
            }

            if (_text[Position] == '0')
            {
                Position++;
            }
            else
            {
                Digits();
            }

            if (!AtEnd && _text[Position] == '.')
            {
                Position++;
                RequireDigit();
                Digits();
            }

            if (!AtEnd && (_text[Position] == 'e' || _text[Position] == 'E'))
            {
                Position++;
                if (!AtEnd && (_text[Position] == '+' || _text[Position] == '-'))
                {
                    Position++;
                }

                RequireDigit();
                Digits();
            }

            return _text.Substring(start, Position - start);
        }

        private void RequireDigit()
        {
            if (AtEnd || !char.IsDigit(_text[Position]))
            {
                throw new JsonReadException("Digit expected", Position);
            }
        }

        private void Digits()
        {
            while (!AtEnd && _text[Position] >= '0' && _text[Position] <= '9')
            {
                Position++;
            }
        }

        private void Expect(string literal)
        {
            if (string.CompareOrdinal(_text, Position, literal, 0, literal.Length) != 0)
            {
                throw new JsonReadException("Invalid literal", Position);
            }

            Position += literal.Length;
        }

        private void Require(char c)
        {
            if (AtEnd || _text[Position] != c)
            {
                throw new JsonReadException($"Expected '{c}'", Position);
            }

            Position++;
        }
    }
}
