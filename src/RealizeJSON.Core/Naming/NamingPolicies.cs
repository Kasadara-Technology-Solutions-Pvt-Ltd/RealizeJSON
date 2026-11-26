using System;
using System.Globalization;
using System.Text;

namespace RealizeJSON.Core.Naming;

/// <summary>
/// Serializer-specific naming algorithms. System.Text.Json and Newtonsoft.Json differ in edge cases
/// (for example separators around digits and existing underscores), so each serializer has its own implementation.
/// Verified by differential tests against the real serializers.
/// </summary>
public static class NamingPolicies
{
    public static string Convert(NamingPolicyKind policy, string name, SerializerKind serializer)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        return policy switch
        {
            NamingPolicyKind.Unchanged => name,
            NamingPolicyKind.CamelCase => ToCamelCase(name, serializer),
            NamingPolicyKind.SnakeCaseLower => serializer == SerializerKind.SystemTextJson ? StjSeparated(name, '_', lowercase: true) : NewtonsoftSeparated(name, '_'),
            NamingPolicyKind.SnakeCaseUpper => serializer == SerializerKind.SystemTextJson ? StjSeparated(name, '_', lowercase: false) : NewtonsoftSeparated(name, '_').ToUpperInvariant(),
            NamingPolicyKind.KebabCaseLower => serializer == SerializerKind.SystemTextJson ? StjSeparated(name, '-', lowercase: true) : NewtonsoftSeparated(name, '-'),
            NamingPolicyKind.KebabCaseUpper => serializer == SerializerKind.SystemTextJson ? StjSeparated(name, '-', lowercase: false) : NewtonsoftSeparated(name, '-').ToUpperInvariant(),
            _ => name,
        };
    }

    /// <summary>True when the serializer ships the policy (Newtonsoft has no upper-case strategies).</summary>
    public static bool IsSupported(NamingPolicyKind policy, SerializerKind serializer) =>
        serializer == SerializerKind.SystemTextJson || policy is not (NamingPolicyKind.SnakeCaseUpper or NamingPolicyKind.KebabCaseUpper);

    /// <summary>Camel case shared by JsonNamingPolicy.CamelCase and Newtonsoft CamelCaseNamingStrategy.</summary>
    private static string ToCamelCase(string s, SerializerKind serializer)
    {
        if (!char.IsUpper(s[0]))
        {
            return s;
        }

        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (i == 1 && !char.IsUpper(chars[i]))
            {
                break;
            }

            var hasNext = i + 1 < chars.Length;
            if (i > 0 && hasNext && !char.IsUpper(chars[i + 1]))
            {
                var separator = serializer == SerializerKind.SystemTextJson ? chars[i + 1] == ' ' : char.IsSeparator(chars[i + 1]);
                if (separator)
                {
                    chars[i] = char.ToLowerInvariant(chars[i]);
                }

                break;
            }

            chars[i] = char.ToLowerInvariant(chars[i]);
        }

        return new string(chars);
    }

    private enum StjState
    {
        NotStarted,
        UppercaseLetter,
        LowercaseLetterOrDigit,
        SpaceSeparator,
    }

    /// <summary>Port of System.Text.Json JsonSeparatorNamingPolicy.</summary>
    private static string StjSeparated(string name, char separator, bool lowercase)
    {
        var sb = new StringBuilder((int)(name.Length * 1.2));
        var state = StjState.NotStarted;
        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];
            var category = char.GetUnicodeCategory(current);
            switch (category)
            {
                case UnicodeCategory.UppercaseLetter:
                    switch (state)
                    {
                        case StjState.LowercaseLetterOrDigit:
                        case StjState.SpaceSeparator:
                            sb.Append(separator);
                            break;
                        case StjState.UppercaseLetter:
                            if (i + 1 < name.Length && char.IsLower(name[i + 1]))
                            {
                                sb.Append(separator);
                            }

                            break;
                    }

                    if (lowercase)
                    {
                        current = char.ToLowerInvariant(current);
                    }

                    sb.Append(current);
                    state = StjState.UppercaseLetter;
                    break;

                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.DecimalDigitNumber:
                    if (state == StjState.SpaceSeparator)
                    {
                        sb.Append(separator);
                    }

                    if (!lowercase && category == UnicodeCategory.LowercaseLetter)
                    {
                        current = char.ToUpperInvariant(current);
                    }

                    sb.Append(current);
                    state = StjState.LowercaseLetterOrDigit;
                    break;

                case UnicodeCategory.SpaceSeparator:
                    if (state != StjState.NotStarted)
                    {
                        state = StjState.SpaceSeparator;
                    }

                    break;

                default:
                    sb.Append(current);
                    state = StjState.NotStarted;
                    break;
            }
        }

        return sb.ToString();
    }

    private enum NsState
    {
        Start,
        Lower,
        Upper,
        NewWord,
    }

    /// <summary>Port of Newtonsoft.Json StringUtils.ToSeparatedCase.</summary>
    private static string NewtonsoftSeparated(string s, char separator)
    {
        var sb = new StringBuilder();
        var state = NsState.Start;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == ' ')
            {
                if (state != NsState.Start)
                {
                    state = NsState.NewWord;
                }
            }
            else if (char.IsUpper(s[i]))
            {
                switch (state)
                {
                    case NsState.Upper:
                        var hasNext = i + 1 < s.Length;
                        if (i > 0 && hasNext)
                        {
                            var next = s[i + 1];
                            if (!char.IsUpper(next) && next != separator)
                            {
                                sb.Append(separator);
                            }
                        }

                        break;
                    case NsState.Lower:
                    case NsState.NewWord:
                        sb.Append(separator);
                        break;
                }

                sb.Append(char.ToLower(s[i], CultureInfo.InvariantCulture));
                state = NsState.Upper;
            }
            else if (s[i] == separator)
            {
                sb.Append(separator);
                state = NsState.Start;
            }
            else
            {
                if (state == NsState.NewWord)
                {
                    sb.Append(separator);
                }

                sb.Append(s[i]);
                state = NsState.Lower;
            }
        }

        return sb.ToString();
    }

    /// <summary>Display label for a policy.</summary>
    public static string Label(NamingPolicyKind policy) => policy switch
    {
        NamingPolicyKind.Unchanged => "Unchanged (as declared)",
        NamingPolicyKind.CamelCase => "camelCase",
        NamingPolicyKind.SnakeCaseLower => "snake_case",
        NamingPolicyKind.SnakeCaseUpper => "SNAKE_CASE",
        NamingPolicyKind.KebabCaseLower => "kebab-case",
        NamingPolicyKind.KebabCaseUpper => "KEBAB-CASE",
        _ => throw new ArgumentOutOfRangeException(nameof(policy)),
    };
}
