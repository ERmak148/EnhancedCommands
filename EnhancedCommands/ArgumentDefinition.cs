using System;
using System.Linq;
using System.Reflection;

namespace EnhancedCommands
{
    public class ArgumentDefinition
    {
        public string Name { get; }
        
        public Type Type { get; }
        
        public bool IsOptional { get; set; } = false;
        
        public bool IsNeedManyWords { get; set; } = false;
        
        public bool IsNamed { get; set; } = false;
        
        public ConstructorInfo Constructor { get; set; }
        
        public ParameterInfo[] ConstructorParameters { get; set; }

        public ArgumentDefinition(string name, Type type, ConstructorInfo constructor = null)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            if (type == null) throw new ArgumentNullException(nameof(type));

            Name = name;
            Type = type;
            Constructor = constructor;

            if (constructor != null)
            {
                ConstructorParameters = constructor.GetParameters();
            }
            else if (!type.IsValueType && type != typeof(string) && !type.IsEnum)
            {
                Constructor = type.GetConstructors()
                    .OrderBy(c => c.GetParameters().Length)
                    .FirstOrDefault();
                ConstructorParameters = Constructor?.GetParameters() ?? Array.Empty<ParameterInfo>();
            }
        }
    }
}