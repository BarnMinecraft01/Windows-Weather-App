using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WeatherApp.Json
{
    /// <summary>
    /// A small recursive-descent JSON reader.
    ///
    /// The app deliberately carries no NuGet dependencies so that the solution
    /// builds from a bare checkout with nothing but MSBuild -- useful on the older
    /// machines this app targets, where package restore is often the first thing
    /// to break.
    /// </summary>
    internal static class JsonParser
    {
        // Guards against a hostile or corrupt payload turning into a stack overflow.
        private const int MaxDepth = 96;

        public static JsonValue Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");

            int index = 0;
            SkipWhitespace(text, ref index);
            JsonValue value = ParseValue(text, ref index, 0);
            SkipWhitespace(text, ref index);

            if (index != text.Length)
            {
                throw new FormatException(
                    "Unexpected trailing content at position " + index.ToString(CultureInfo.InvariantCulture) + ".");
            }
            return value;
        }

        private static JsonValue ParseValue(string text, ref int index, int depth)
        {
            if (depth > MaxDepth) throw new FormatException("JSON nested too deeply.");
            if (index >= text.Length) throw new FormatException("Unexpected end of JSON.");

            char c = text[index];
            switch (c)
            {
                case '{': return ParseObject(text, ref index, depth);
                case '[': return ParseArray(text, ref index, depth);
                case '"': return new JsonValue(ParseString(text, ref index));
                case 't': Expect(text, ref index, "true"); return new JsonValue(true);
                case 'f': Expect(text, ref index, "false"); return new JsonValue(false);
                case 'n': Expect(text, ref index, "null"); return JsonValue.Null;
                default: return ParseNumber(text, ref index);
            }
        }

        private static JsonValue ParseObject(string text, ref int index, int depth)
        {
            index++; // consume '{'
            var map = new Dictionary<string, JsonValue>(StringComparer.Ordinal);

            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == '}')
            {
                index++;
                return new JsonValue(map);
            }

            while (true)
            {
                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] != '"')
                {
                    throw new FormatException("Expected an object key at position "
                        + index.ToString(CultureInfo.InvariantCulture) + ".");
                }

                string key = ParseString(text, ref index);
                SkipWhitespace(text, ref index);

                if (index >= text.Length || text[index] != ':')
                {
                    throw new FormatException("Expected ':' after object key \"" + key + "\".");
                }
                index++;

                SkipWhitespace(text, ref index);
                map[key] = ParseValue(text, ref index, depth + 1);
                SkipWhitespace(text, ref index);

                if (index >= text.Length) throw new FormatException("Unterminated JSON object.");
                if (text[index] == ',') { index++; continue; }
                if (text[index] == '}') { index++; return new JsonValue(map); }

                throw new FormatException("Expected ',' or '}' at position "
                    + index.ToString(CultureInfo.InvariantCulture) + ".");
            }
        }

        private static JsonValue ParseArray(string text, ref int index, int depth)
        {
            index++; // consume '['
            var items = new List<JsonValue>();

            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == ']')
            {
                index++;
                return new JsonValue(items);
            }

            while (true)
            {
                SkipWhitespace(text, ref index);
                items.Add(ParseValue(text, ref index, depth + 1));
                SkipWhitespace(text, ref index);

                if (index >= text.Length) throw new FormatException("Unterminated JSON array.");
                if (text[index] == ',') { index++; continue; }
                if (text[index] == ']') { index++; return new JsonValue(items); }

                throw new FormatException("Expected ',' or ']' at position "
                    + index.ToString(CultureInfo.InvariantCulture) + ".");
            }
        }

        private static string ParseString(string text, ref int index)
        {
            index++; // consume opening quote
            var builder = new StringBuilder();

            while (index < text.Length)
            {
                char c = text[index++];

                if (c == '"') return builder.ToString();

                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (index >= text.Length) break;
                char escape = text[index++];
                switch (escape)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (index + 4 > text.Length) throw new FormatException("Truncated \\u escape.");
                        string hex = text.Substring(index, 4);
                        int code;
                        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                        {
                            throw new FormatException("Invalid \\u escape \"" + hex + "\".");
                        }
                        builder.Append((char)code);
                        index += 4;
                        break;
                    default:
                        throw new FormatException("Unrecognised escape \"\\" + escape + "\".");
                }
            }

            throw new FormatException("Unterminated JSON string.");
        }

        private static JsonValue ParseNumber(string text, ref int index)
        {
            int start = index;

            if (index < text.Length && (text[index] == '-' || text[index] == '+')) index++;
            while (index < text.Length)
            {
                char c = text[index];
                bool isNumeric = (c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-';
                if (!isNumeric) break;
                index++;
            }

            if (index == start)
            {
                throw new FormatException("Unexpected character '" + text[index]
                    + "' at position " + index.ToString(CultureInfo.InvariantCulture) + ".");
            }

            string slice = text.Substring(start, index - start);
            double parsed;
            if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                throw new FormatException("Invalid number \"" + slice + "\".");
            }
            return new JsonValue(parsed);
        }

        private static void Expect(string text, ref int index, string literal)
        {
            if (index + literal.Length > text.Length
                || string.CompareOrdinal(text, index, literal, 0, literal.Length) != 0)
            {
                throw new FormatException("Expected \"" + literal + "\" at position "
                    + index.ToString(CultureInfo.InvariantCulture) + ".");
            }
            index += literal.Length;
        }

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length)
            {
                char c = text[index];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') index++;
                else break;
            }
        }
    }
}
