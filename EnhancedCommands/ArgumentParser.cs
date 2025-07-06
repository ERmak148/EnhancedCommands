using System;
using System.Linq;
using System.Reflection;
using Exiled.API.Features;

namespace EnhancedCommands
{
    public static class ArgumentParser
    {
        public static bool TryParse(string value, Type type, out object result, out string error, ConstructorInfo constructor = null)
        {
            result = null;
            error = string.Empty;

            // Обработка простых типов
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
            
            var argValues = value.Split(',').Select(v => v.Trim()).ToArray();
            if (argValues.Length != parameters.Length)
            {
                error = $"Expected {parameters.Length} arguments for '{type.Name}', but got {argValues.Length}.";
                return false;
            }

            var parsedParams = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var paramType = parameters[i].ParameterType;
                if (!TryParse(argValues[i], paramType, out var paramResult, out error))
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
                error = $"Failed to create instance of '{type.Name}' with provided arguments: {ex.Message}";
                return false;
            }
        }
    }
}