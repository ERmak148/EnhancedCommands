using System;

namespace EnhancedCommands
{
    [AttributeUsage(AttributeTargets.Class)]
    public class Command : Attribute
    {
        public string Name { get; }
        public string[] Aliases { get; }
        public string Description { get; }

        public Command(string name, string[] aliases = null, string description = "No description provided.")
        {
            Name = name;
            Aliases = aliases ?? new string[0];
            Description = description;
        }
    }
}