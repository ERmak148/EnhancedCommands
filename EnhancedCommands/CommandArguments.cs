using System;
using System.Linq;
using Exiled.API.Features;

namespace EnhancedCommands
{
     public class CommandArguments
    {
        private readonly string[] _args;

        public int Count => _args.Length;

        public CommandArguments(ArraySegment<string> arguments)
        {
            _args = arguments.ToArray();
        }
        
        public string this[int index] => index >= 0 && index < _args.Length ? _args[index] : null;
        
        public bool TryGetPlayer(int index, out Player player)
        {
            player = null;
            string query = this[index];
            if (query == null)
                return false;

            player = Player.Get(query);
            return player != null;
        }
        
        public bool TryGetInt(int index, out int value) => int.TryParse(this[index], out value);
        
        public bool TryGetFloat(int index, out float value) => float.TryParse(this[index], out value);
        
        public bool TryGetBool(int index, out bool value)
        {
            value = false;
            string arg = this[index]?.ToLower();
            if (arg == null) return false;

            switch (arg)
            {
                case "true":
                case "1":
                case "yes":
                case "y":
                    value = true;
                    return true;
                case "false":
                case "0":
                case "no":
                case "n":
                    value = false;
                    return true;
                default:
                    return bool.TryParse(arg, out value);
            }
        }
        
        public bool TryGetEnum<T>(int index, out T enumValue) where T : struct, Enum
        {
            enumValue = default;
            string arg = this[index];
            if (arg == null) return false;
            
            return Enum.TryParse(arg, true, out enumValue);
        }
        
        public string Join(int startIndex = 0) => string.Join(" ", _args.Skip(startIndex));
    }
}