using System;
using Exiled.API.Features;

namespace EnhancedCommands
{
    public static class ArgumentParser
    {
        public static bool TryParse(string value, Type type, out object result, out string error)
        {
            result = null;
            error = string.Empty;

            if (type == typeof(string)) { result = value; return true; }
            if (type == typeof(int)) { if (int.TryParse(value, out var i)) { result = i; return true; } error = "Expected a whole number."; return false; }
            if (type == typeof(float)) { if (float.TryParse(value, out var f)) { result = f; return true; } error = "Expected a number."; return false; }
            if (type == typeof(double)) { if (double.TryParse(value, out var d)) { result = d; return true; } error = "Expected a number."; return false; }
            if (type == typeof(byte)) { if (byte.TryParse(value, out var b)) { result = b; return true; } error = "Expected a number between 0 and 255."; return false; }
            if (type == typeof(bool)) { var args = new CommandArguments(new ArraySegment<string>(new []{value})); if (args.TryGetBool(0, out var bl)) { result = bl; return true; } error = "Expected true/false, yes/no, or 1/0."; return false; }
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
            if (type == typeof(Player)) { var p = Player.Get(value); if (p != null) { result = p; return true; } error = "Player not found."; return false; }
            
            error = $"Unsupported argument type '{type.Name}'.";
            return false;
        }
    }
}