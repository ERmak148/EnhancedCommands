using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Exiled.API.Features;

namespace EnhancedCommands
{
    // I've just rewrited this class with gpt because I'm too lazy 
    public static class ArgumentParser
    {
        private const int MaxRecursionDepth = 10;

        [ThreadStatic]
        private static int _recursionDepth;

        public static bool TryParse(string value, Type type, out object result, out string error,
            ConstructorInfo constructor = null)
        {
            result = null;
            error = string.Empty;

            if (_recursionDepth >= MaxRecursionDepth)
            {
                error = "Maximum parsing recursion depth exceeded.";
                return false;
            }

            _recursionDepth++;
            try
            {
                return TryParseInternal(value, type, out result, out error, constructor);
            }
            finally
            {
                _recursionDepth--;
            }
        }

        private static bool TryParseInternal(string value, Type type, out object result, out string error,
            ConstructorInfo constructor = null)
        {
            result = null;
            error = string.Empty;

            if (type == null)
            {
                error = "Type cannot be null.";
                return false;
            }

            var underlyingType = Nullable.GetUnderlyingType(type);
            if (underlyingType != null)
            {
                if (string.IsNullOrWhiteSpace(value) || value.Equals("null", StringComparison.OrdinalIgnoreCase))
                {
                    result = null;
                    return true;
                }
                type = underlyingType;
            }

            if (type == typeof(string))
            {
                result = value ?? string.Empty;
                return true;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                error = "Value cannot be empty.";
                return false;
            }

            if (type == typeof(int))
            {
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                {
                    result = i;
                    return true;
                }
                error = "Expected a whole number.";
                return false;
            }

            if (type == typeof(float))
            {
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                {
                    result = f;
                    return true;
                }
                error = "Expected a number.";
                return false;
            }

            if (type == typeof(double))
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                {
                    result = d;
                    return true;
                }
                error = "Expected a number.";
                return false;
            }

            if (type == typeof(byte))
            {
                if (byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
                {
                    result = b;
                    return true;
                }
                error = "Expected a number between 0 and 255.";
                return false;
            }

            if (type == typeof(bool))
            {
                var args = new CommandArguments(new ArraySegment<string>(new[] { value }));
                if (args.TryGetBool(0, out var bl))
                {
                    result = bl;
                    return true;
                }
                error = "Expected true/false, yes/no, or 1/0.";
                return false;
            }

            if (type.IsEnum)
            {
                try
                {
                    result = Enum.Parse(type, value, true);

                    if (!Enum.IsDefined(type, result))
                    {
                        var hasFlags = type.GetCustomAttributes(typeof(FlagsAttribute), false).Length > 0;
                        if (!hasFlags)
                        {
                            error = $"Value '{value}' is not defined in {type.Name}. Expected one of: {string.Join(", ", Enum.GetNames(type))}.";
                            return false;
                        }
                    }

                    return true;
                }
                catch (ArgumentException)
                {
                    error = $"Expected one of: {string.Join(", ", Enum.GetNames(type))}.";
                    return false;
                }
                catch (OverflowException)
                {
                    error = $"Value '{value}' is outside the range of {type.Name}.";
                    return false;
                }
            }

            if (type == typeof(Player))
            {
                var player = Player.Get(value);
                if (player == null)
                {
                    error = "Player not found.";
                    return false;
                }

                result = player;
                return true;
            }

            if (type == typeof(List<Player>))
            {
                var players = new List<Player>();
                var playerSet = new HashSet<Player>();
                var elements = SplitArguments(value);

                if (elements.Length == 0)
                {
                    result = players;
                    return true;
                }

                foreach (var el in elements)
                {
                    if (string.IsNullOrWhiteSpace(el))
                        continue;

                    if (el == "*")
                    {
                        foreach (var p in Player.List)
                        {
                            if (playerSet.Add(p))
                            {
                                players.Add(p);
                            }
                        }
                    }
                    else
                    {
                        var foundPlayer = Player.Get(el);
                        if (foundPlayer == null)
                        {
                            error = $"Player not found: '{el}'.";
                            return false;
                        }

                        if (playerSet.Add(foundPlayer))
                        {
                            players.Add(foundPlayer);
                        }
                    }
                }

                result = players;
                return true;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var elementType = type.GetGenericArguments()[0];
                var list = (System.Collections.IList)Activator.CreateInstance(type);

                var elements = SplitArguments(value);

                if (elements.Length == 0)
                {
                    result = list;
                    return true;
                }

                foreach (var el in elements)
                {
                    if (string.IsNullOrWhiteSpace(el))
                        continue;

                    if (!TryParse(el, elementType, out var parsedEl, out error))
                    {
                        error = $"Failed to parse list element '{el}': {error}";
                        return false;
                    }

                    list.Add(parsedEl);
                }

                result = list;
                return true;
            }

            if (constructor == null)
            {
                var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
                constructor = constructors
                    .OrderBy(c => c.GetParameters().Length)
                    .FirstOrDefault();
            }

            if (constructor == null)
            {
                error = $"No public constructor found for type '{type.Name}'.";
                return false;
            }

            var parameters = constructor.GetParameters();
            if (parameters.Length == 0)
            {
                try
                {
                    result = Activator.CreateInstance(type);
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"Failed to create instance of '{type.Name}': {ex.Message}";
                    return false;
                }
            }

            var argValues = SplitArguments(value);
            if (argValues.Length != parameters.Length)
            {
                error = $"Expected {parameters.Length} constructor arguments for '{type.Name}', but got {argValues.Length}.";
                return false;
            }

            var parsedParams = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                if (!TryParse(argValues[i], parameters[i].ParameterType, out var paramResult, out error))
                {
                    error = $"Failed to parse parameter '{parameters[i].Name}' for '{type.Name}': {error}";
                    return false;
                }

                parsedParams[i] = paramResult;
            }

            try
            {
                result = constructor.Invoke(parsedParams);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to create instance of '{type.Name}': {ex.InnerException?.Message ?? ex.Message}";
                return false;
            }
        }

        private static string[] SplitArguments(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return Array.Empty<string>();

            input = input.Trim();
            if (input.Length == 0)
                return Array.Empty<string>();

            char openChar = input[0];
            string inner;

            if (openChar == '(' || openChar == '{' || openChar == '[')
            {
                char closeChar = openChar switch
                {
                    '(' => ')',
                    '{' => '}',
                    '[' => ']',
                    _ => throw new ArgumentException($"Unexpected opening character: '{openChar}'")
                };

                if (input.Length < 2 || input[input.Length - 1] != closeChar)
                {
                    inner = input;
                    return ParseSeparatedArguments(inner, ',', false, false);
                }

                inner = input.Substring(1, input.Length - 2).Trim();
                return ParseSeparatedArguments(inner, ' ', true, true);
            }
            else
            {
                inner = input;
                return ParseSeparatedArguments(inner, ',', false, false);
            }
        }

        private static bool IsEscaped(string str, int index)
        {
            if (index == 0) return false;
            int backslashCount = 0;
            for (int i = index - 1; i >= 0 && str[i] == '\\'; i--)
            {
                backslashCount++;
            }
            return backslashCount % 2 == 1;
        }

        private static string[] ParseSeparatedArguments(string input, char separator, bool allowSpaceSeparator,
            bool allowNested)
        {
            if (string.IsNullOrWhiteSpace(input))
                return Array.Empty<string>();

            var results = new List<string>();
            var current = new StringBuilder();
            int nestLevel = 0;
            bool inQuotes = false;
            var nestStack = new Stack<char>();

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (c == '"' && !IsEscaped(input, i))
                {
                    inQuotes = !inQuotes;
                    current.Append(c);
                    continue;
                }

                if (!inQuotes && allowNested)
                {
                    if (c == '[' || c == '(' || c == '{')
                    {
                        nestStack.Push(c);
                        nestLevel++;
                    }
                    else if (c == ']' || c == ')' || c == '}')
                    {
                        if (nestStack.Count > 0)
                        {
                            var expected = nestStack.Peek();
                            if ((expected == '[' && c == ']') ||
                                (expected == '(' && c == ')') ||
                                (expected == '{' && c == '}'))
                            {
                                nestStack.Pop();
                                nestLevel--;
                            }
                            else
                            {
                                throw new ArgumentException($"Mismatched bracket: expected '{GetClosingBracket(expected)}' but found '{c}'.");
                            }
                        }
                        else
                        {
                            throw new ArgumentException($"Unexpected closing bracket '{c}' without matching opening bracket.");
                        }
                    }
                }

                if ((c == separator || (allowSpaceSeparator && c == ' ')) && nestLevel == 0 && !inQuotes)
                {
                    var trimmed = current.ToString().Trim();
                    if (trimmed.Length > 0)
                    {
                        results.Add(UnquoteIfNeeded(trimmed));
                    }
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            if (inQuotes)
            {
                throw new ArgumentException("Unclosed quotation mark in input.");
            }

            if (nestStack.Count > 0)
            {
                var unclosed = nestStack.Peek();
                throw new ArgumentException($"Unclosed bracket '{unclosed}', expected '{GetClosingBracket(unclosed)}'.");
            }

            if (current.Length > 0)
            {
                var trimmed = current.ToString().Trim();
                if (trimmed.Length > 0)
                {
                    results.Add(UnquoteIfNeeded(trimmed));
                }
            }

            return results.ToArray();
        }

        private static char GetClosingBracket(char openBracket)
        {
            return openBracket switch
            {
                '[' => ']',
                '(' => ')',
                '{' => '}',
                _ => '\0'
            };
        }

        private static string UnquoteIfNeeded(string str)
        {
            if (string.IsNullOrEmpty(str) || str.Length < 2)
                return str;

            if (str[0] == '"' && str[str.Length - 1] == '"' && !IsEscaped(str, str.Length - 1))
            {
                return str.Substring(1, str.Length - 2);
            }

            return str;
        }
    }
}