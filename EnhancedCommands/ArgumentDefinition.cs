using System;

namespace EnhancedCommands
{
    public class ArgumentDefinition
    {
        public string Name { get; }
        
        public Type Type { get; }
        
        public bool IsOptional { get; set; } = false;
        
        public bool IsNeedManyWords { get; set; } = false;

        public ArgumentDefinition(string name, Type type)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            if (type == null) throw new ArgumentNullException(nameof(type));
            
            Name = name;
            Type = type;
        }
    }
}