using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Exiled.API.Features;

namespace EnhancedCommands
{
    public static class ArgumentParser
    {
        public static bool TryParse(string value, Type type, out object result, out string error,
            ConstructorInfo constructor = null)
        {
            result = null;
            error = string.Empty;

            if (type == typeof(string))
            {
                result = value;
                return true;
            }

            if (type == typeof(int))
            {
                if (int.TryParse(value, out var i))
                {
                    result = i;
                    return true;
                }

                error = "Expected a whole number.";
                return false;
            }

            if (type == typeof(float))
            {
                if (float.TryParse(value, out var f))
                {
                    result = f;
                    return true;
                }

                error = "Expected a number.";
                return false;
            }

            if (type == typeof(double))
            {
                if (double.TryParse(value, out var d))
                {
                    result = d;
                    return true;
                }

                error = "Expected a number.";
                return false;
            }

            if (type == typeof(byte))
            {
                if (byte.TryParse(value, out var b))
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
                    return true;
                }
                catch (ArgumentException)
                {
                    error = $"Expected one of: {string.Join(", ", Enum.GetNames(type))}.";
                    return false;
                }
            }

            if (type == typeof(Player))
            {
                var p = Player.Get(value);
                if (p != null)
                {
                    result = p;
                    return true;
                }

                error = "Player not found.";
                return false;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var elementType = type.GetGenericArguments()[0];
                var list = (System.Collections.IList)Activator.CreateInstance(type);

                var elements = SplitArguments(value);
                foreach (var el in elements)
                {
                    if (!TryParse(el, elementType, out var parsedEl, out error))
                    {
                        error = $"Failed to parse list element: {error}";
                        return false;
                    }

                    list.Add(parsedEl);
                }

                result = list;
                return true;
            }

            if (type == typeof(List<Player>))
            {
                var players = new List<Player>();
                var elements = SplitArguments(value);

                foreach (var el in elements)
                {
                    if (el == "*")
                    {
                        players.AddRange(Player.List);
                    }
                    else
                    {
                        var p = Player.Get(el);
                        if (p != null)
                        {
                            players.Add(p);
                        }
                        else
                        {
                            error = $"Player not found: '{el}'.";
                            return false;
                        }
                    }
                }

                result = players;
                return true;
            }

            if (constructor == null)
            {
                constructor = type.GetConstructors()
                    .OrderBy(c => c.GetParameters().Length)
                    .FirstOrDefault();
            }

            if (constructor == null)
            {
                error = $"No suitable constructor found for type '{type.Name}'.";
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
                error =
                    $"Expected {parameters.Length} constructor arguments for '{type.Name}', but got {argValues.Length}.";
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
                error =
                    $"Failed to create instance of '{type.Name}' with provided arguments: {ex.InnerException?.Message ?? ex.Message}";
                return false;
            }
        }

        private static string[] SplitArguments(string input)
        {
            input = input.Trim();
            if (input.Length == 0) return Array.Empty<string>();

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

                if (!input.EndsWith(closeChar.ToString()))
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

        private static string[] ParseSeparatedArguments(string input, char separator, bool allowSpaceSeparator,
            bool allowNested)
        {
            var results = new List<string>();
            var current = new StringBuilder();
            int nestLevel = 0;
            bool inQuotes = false;
            char? nestOpen = null;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (c == '"' && (i == 0 || input[i - 1] != '\\'))
                {
                    inQuotes = !inQuotes;
                    current.Append(c);
                    continue;
                }

                if (!inQuotes && allowNested)
                {
                    if (c == '[' || c == '(' || c == '{')
                    {
                        if (nestLevel == 0)
                        {
                            nestOpen = c;
                        }

                        nestLevel++;
                    }
                    else if ((c == ']' && nestOpen == '[') || (c == ')' && nestOpen == '(') ||
                             (c == '}' && nestOpen == '{'))
                    {
                        nestLevel--;
                    }
                }

                if ((c == separator || (allowSpaceSeparator && c == ' ')) && nestLevel == 0 && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        results.Add(current.ToString().Trim());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0)
            {
                results.Add(current.ToString().Trim());
            }

            return results.ToArray();
        }
    }
}