using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using CommandSystem;
using Exiled.API.Features;
using Exiled.Permissions.Extensions;

namespace EnhancedCommands
{
    public abstract class ParentCommandBase : ICommand, IUsageProvider
    {
        private readonly Command _commandAttribute;
        private readonly CommandPermission _permissionAttribute;
        private readonly Dictionary<string, ICommand> _subCommands = new Dictionary<string, ICommand>(StringComparer.OrdinalIgnoreCase);

        public string Command { get; }
        public string[] Aliases { get; }
        public string Description { get; }
        protected abstract IReadOnlyList<ICommand> SubCommands { get; }
        public string[] Usage { get; }

        protected ParentCommandBase()
        {
            Type type = GetType();
            _commandAttribute = type.GetCustomAttribute<Command>();
            _permissionAttribute = type.GetCustomAttribute<CommandPermission>();

            if (_commandAttribute == null)
                throw new ArgumentException($"Parent command {type.Name} is missing the [Command] attribute.");

            Command = _commandAttribute.Name;
            Aliases = _commandAttribute.Aliases;
            Description = _commandAttribute.Description;
            Usage = new[] { "<subcommand> [arguments...]" };

            RegisterSubCommands();
        }
        
        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (_permissionAttribute != null && !sender.CheckPermission(_permissionAttribute.Permission))
            {
                response = $"You do not have permission to use this command. Required: {_permissionAttribute.Permission}";
                return false;
            }

            if (arguments.Count == 0)
            {
                try
                {
                    var context = new CommandContext(sender, new CommandArguments(arguments));
                    var cresponse = OnExecuteParent(context);
                    response = cresponse.Message;
                    return cresponse.IsSuccess;
                }
                catch (NotImplementedException)
                {
                    response = GenerateHelpMessage();
                    return false;
                }
            }

            string subCommandName = arguments.At(0);
            if (!_subCommands.TryGetValue(subCommandName, out ICommand subCommand))
            {
                response = $"Unknown subcommand '{subCommandName}'.\n{GenerateHelpMessage()}";
                return false;
            }
            
            var subCommandArguments = new ArraySegment<string>(arguments.Array, arguments.Offset + 1, arguments.Count - 1);
            
            return subCommand.Execute(subCommandArguments, sender, out response);
        }

        public virtual CommandResponse OnExecuteParent(CommandContext context) => throw new NotImplementedException($"OnExecuteParent is not defined");

        private void RegisterSubCommands()
        {
            if (SubCommands == null || SubCommands.Count == 0)
            {
                Log.Warn($"Parent command '{Command}' has no registered sub-commands.");
                return;
            }

            foreach (var command in SubCommands)
            {
                if (string.IsNullOrWhiteSpace(command.Command))
                    continue;

                if (_subCommands.ContainsKey(command.Command))
                {
                    Log.Error($"Duplicate sub-command name or alias '{command.Command}' in parent command '{Command}'.");
                    continue;
                }
                _subCommands.Add(command.Command, command);

                if (command.Aliases == null)
                    continue;

                foreach (var alias in command.Aliases)
                {
                    if (string.IsNullOrWhiteSpace(alias))
                        continue;
                        
                    if (_subCommands.ContainsKey(alias))
                    {
                        Log.Error($"Duplicate sub-command name or alias '{alias}' in parent command '{Command}'.");
                        continue;
                    }
                    _subCommands.Add(alias, command);
                }
            }
        }

        private string GenerateHelpMessage()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Description}");
            sb.AppendLine($"Usage: {Command} {Usage.FirstOrDefault()}");
            sb.AppendLine("Available subcommands:");
            
            foreach (var command in SubCommands.Distinct())
            {
                string usageHint = (command as IUsageProvider)?.Usage.FirstOrDefault() ?? "[args...]";
                string description = "No description.";
                if (command is SyncCommand syncCmd)
                {
                    description = syncCmd.Description;
                }
                else if (command is AsyncCommand asyncCmd)
                {
                    description = asyncCmd.Description;
                }
                sb.AppendLine($"  - {command.Command} {usageHint}: {description}");
            }

            return sb.ToString();
        }
    }
}