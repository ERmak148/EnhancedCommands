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
        public static bool TryParse(string value, Type type, out object result, out string error, ConstructorInfo constructor = null)
        {
            result = null;
            error = string.Empty;

            if (type == typeof(string)) { result = value; return true; }
            if (type == typeof(int)) { if (int.TryParse(value, out var i)) { result = i; return true; } error = "Expected a whole number."; return false; }
            if (type == typeof(float)) { if (float.TryParse(value, out var f)) { result = f; return true; } error = "Expected a number."; return false; }
            if (type == typeof(double)) { if (double.TryParse(value, out var d)) { result = d; return true; } error = "Expected a number."; return false; }
            if (type == typeof(byte)) { if (byte.TryParse(value, out var b)) { result = b; return true; } error = "Expected a number between 0 and 255."; return false; }
            if (type == typeof(bool))
            {
                var args = new CommandArguments(new ArraySegment<string>(new[] { value }));
                if (args.TryGetBool(0, out var bl)) { result = bl; return true; }
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
                if (p != null) { result = p; return true; }
                error = "Player not found.";
                return false;
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
                error = $"Failed to create instance of '{type.Name}' with provided arguments: {ex.InnerException?.Message ?? ex.Message}";
                return false;
            }
        }
        
        private static string[] SplitArguments(string input)
        {
            input = input.Trim();
            if (input.StartsWith("[") && input.EndsWith("]"))
            {
                string inner = input.Substring(1, input.Length - 2);
                return ParseBracketedArguments(inner);
            }
            else
            {
                return ParseCommaSeparatedArguments(input);
            }
        }
        
        private static string[] ParseBracketedArguments(string input)
        {
            var results = new List<string>();
            var currentArgument = new StringBuilder();
            int bracketDepth = 0;
            bool inQuotes = false;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (c == '"' && (i == 0 || input[i - 1] != '\\'))
                {
                    inQuotes = !inQuotes;
                }
                else if (c == '[' && !inQuotes)
                {
                    bracketDepth++;
                }
                else if (c == ']' && !inQuotes)
                {
                    bracketDepth--;
                }
                else if (c == ' ' && !inQuotes && bracketDepth == 0)
                {
                    if (currentArgument.Length > 0)
                    {
                        results.Add(currentArgument.ToString().Trim());
                        currentArgument.Clear();
                    }
                    continue;
                }

                currentArgument.Append(c);
            }

            if (currentArgument.Length > 0)
            {
                results.Add(currentArgument.ToString().Trim());
            }

            return results.ToArray();
        }

        private static string[] ParseCommaSeparatedArguments(string input)
        {
            var results = new List<string>();
            var current = new StringBuilder();
            int bracketLevel = 0;
            bool inQuotes = false;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c == '[' && !inQuotes) bracketLevel++;
                else if (c == ']' && !inQuotes) bracketLevel--;
                else if (c == '"' && (i == 0 || input[i - 1] != '\\')) inQuotes = !inQuotes;
                else if (c == ',' && bracketLevel == 0 && !inQuotes)
                {
                    results.Add(current.ToString().Trim());
                    current.Clear();
                    continue;
                }
                
                current.Append(c);
            }

            if (current.Length > 0)
            {
                results.Add(current.ToString().Trim());
            }

            return results.ToArray();
        }
    }
}