using System;

namespace EnhancedCommands
{
    [AttributeUsage(AttributeTargets.Class)]
    public class CommandPermission : Attribute
    {
        public string Permission { get; }

        public CommandPermission(string permission)
        {
            Permission = permission;
        }
    }
}