using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Arkon.Core
{
    /// <summary>
    /// Strict JSON reader shared with Unity's .NET Standard 2.1 profile.
    /// Numbers are finite doubles; objects use ordinal, duplicate-free keys.
    /// </summary>
    public static class PortableJson
    {
        public static object Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            return new Reader(json).Document();
        }

        private sealed class Reader
        {
            private readonly string _text;
            private int _position;
            public Reader(string text) { _text = text; }

            public object Document()
            {
                var value = Value(0);
                WhiteSpace();
                if (_position != _text.Length) throw Error("Unexpected content after JSON value");
                return value;
            }

            private object Value(int depth)
            {
                if (depth > 128) throw Error("JSON nesting exceeds 128 levels");
                WhiteSpace();
                if (_position >= _text.Length) throw Error("Expected JSON value");
                switch (_text[_position])
                {
                    case '{': return Object(depth + 1);
                    case '[': return Array(depth + 1);
                    case '"': return String();
                    case 't': Literal("true"); return true;
                    case 'f': Literal("false"); return false;
                    case 'n': Literal("null"); return null;
                    default:
                        if (_text[_position] == '-' || Digit(_text[_position])) return Number();
                        throw Error("Expected JSON value");
                }
            }

            private Dictionary<string, object> Object(int depth)
            {
                _position++;
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                WhiteSpace();
                if (Take('}')) return result;
                while (true)
                {
                    WhiteSpace();
                    if (_position >= _text.Length || _text[_position] != '"') throw Error("Expected object key");
                    var key = String();
                    if (result.ContainsKey(key)) throw Error("Duplicate object key");
                    WhiteSpace(); Require(':');
                    result.Add(key, Value(depth));
                    WhiteSpace();
                    if (Take('}')) return result;
                    Require(',');
                }
            }

            private List<object> Array(int depth)
            {
                _position++;
                var result = new List<object>();
                WhiteSpace();
                if (Take(']')) return result;
                while (true)
                {
                    result.Add(Value(depth));
                    WhiteSpace();
                    if (Take(']')) return result;
                    Require(',');
                }
            }

            private string String()
            {
                Require('"');
                var result = new StringBuilder();
                while (_position < _text.Length)
                {
                    var c = _text[_position++];
                    if (c == '"') return result.ToString();
                    if (c < 0x20) throw Error("Unescaped control character in string");
                    if (c == '\\')
                    {
                        if (_position >= _text.Length) throw Error("Incomplete string escape");
                        switch (_text[_position++])
                        {
                            case '"': result.Append('"'); break;
                            case '\\': result.Append('\\'); break;
                            case '/': result.Append('/'); break;
                            case 'b': result.Append('\b'); break;
                            case 'f': result.Append('\f'); break;
                            case 'n': result.Append('\n'); break;
                            case 'r': result.Append('\r'); break;
                            case 't': result.Append('\t'); break;
                            case 'u':
                                var code = Unicode();
                                if (char.IsHighSurrogate(code))
                                {
                                    if (_position + 1 >= _text.Length || _text[_position] != '\\' || _text[_position + 1] != 'u')
                                        throw Error("Missing low Unicode surrogate");
                                    _position += 2;
                                    var low = Unicode();
                                    if (!char.IsLowSurrogate(low)) throw Error("Invalid low Unicode surrogate");
                                    result.Append(code).Append(low);
                                }
                                else
                                {
                                    if (char.IsLowSurrogate(code)) throw Error("Unpaired low Unicode surrogate");
                                    result.Append(code);
                                }
                                break;
                            default: throw Error("Unknown string escape");
                        }
                    }
                    else if (char.IsHighSurrogate(c))
                    {
                        if (_position >= _text.Length || !char.IsLowSurrogate(_text[_position])) throw Error("Unpaired Unicode surrogate");
                        result.Append(c).Append(_text[_position++]);
                    }
                    else
                    {
                        if (char.IsLowSurrogate(c)) throw Error("Unpaired Unicode surrogate");
                        result.Append(c);
                    }
                }
                throw Error("Unterminated string");
            }

            private char Unicode()
            {
                if (_position + 4 > _text.Length) throw Error("Incomplete Unicode escape");
                var code = 0;
                for (var i = 0; i < 4; i++)
                {
                    var c = _text[_position++];
                    var digit = c >= '0' && c <= '9' ? c - '0' :
                        c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
                    if (digit < 0) throw Error("Invalid Unicode escape");
                    code = code * 16 + digit;
                }
                return (char)code;
            }

            private double Number()
            {
                var start = _position;
                Take('-');
                if (_position >= _text.Length) throw Error("Incomplete number");
                if (Take('0'))
                {
                    if (_position < _text.Length && Digit(_text[_position])) throw Error("Leading zero in number");
                }
                else
                {
                    if (_text[_position] < '1' || _text[_position] > '9') throw Error("Expected number digit");
                    while (_position < _text.Length && Digit(_text[_position])) _position++;
                }
                if (Take('.')) Digits();
                if (Take('e') || Take('E'))
                {
                    if (!Take('+')) Take('-');
                    Digits();
                }
                if (!double.TryParse(_text.Substring(start, _position - start), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var value) || double.IsNaN(value) || double.IsInfinity(value))
                    throw Error("Number is outside the finite double range");
                return value;
            }

            private void Digits()
            {
                var start = _position;
                while (_position < _text.Length && Digit(_text[_position])) _position++;
                if (_position == start) throw Error("Expected number digit");
            }

            private void Literal(string value)
            {
                for (var i = 0; i < value.Length; i++)
                    if (_position >= _text.Length || _text[_position++] != value[i]) throw Error("Invalid JSON literal");
            }

            private void WhiteSpace()
            {
                while (_position < _text.Length)
                {
                    var c = _text[_position];
                    if (c != ' ' && c != '\t' && c != '\r' && c != '\n') return;
                    _position++;
                }
            }

            private static bool Digit(char c) => c >= '0' && c <= '9';
            private bool Take(char c)
            {
                if (_position >= _text.Length || _text[_position] != c) return false;
                _position++;
                return true;
            }
            private void Require(char c)
            {
                if (!Take(c)) throw Error("Expected '" + c + "'");
            }
            private FormatException Error(string message) => new FormatException(message + " at character " + _position + ".");
        }
    }
}
