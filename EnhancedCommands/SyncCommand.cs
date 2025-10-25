using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CommandSystem;
using Exiled.API.Features;
using Exiled.Permissions.Extensions;

namespace EnhancedCommands
{
    public abstract class SyncCommand : ICommand, IUsageProvider
    {
        private readonly Command _commandAttribute;
        private readonly CommandPermission _permissionAttribute;

        public string Command { get; }
        public string[] Aliases { get; }
        public string Description { get; }

        public virtual int MinArgs { get; protected set; }
        public virtual string[] Usage { get; protected set; }

        public virtual IReadOnlyList<ArgumentDefinition> ArgumentsDefinition { get; } = null;

        public virtual bool SanitizeResponse { get; } = false;

        protected SyncCommand()
        {
            _commandAttribute = GetType().GetCustomAttribute<Command>();
            _permissionAttribute = GetType().GetCustomAttribute<CommandPermission>();

            if (_commandAttribute == null)
                throw new ArgumentException($"Command {GetType().Name} is missing the [Command] attribute.");

            Command = _commandAttribute.Name;
            Aliases = _commandAttribute.Aliases;
            Description = _commandAttribute.Description;

            CommandArgPreParser.ValidateArgumentsDefinition(ArgumentsDefinition, Command);

            if (ArgumentsDefinition != null && ArgumentsDefinition.Any())
            {
                MinArgs = ArgumentsDefinition.Count(a => !a.IsOptional);
                Usage = new[] { CommandArgPreParser.GenerateUsageFromDefinition(ArgumentsDefinition) };
            }
        }

        protected virtual CommandResponse OnExecuteSync(CommandContext context) =>
            throw new NotImplementedException($"Command {GetType().Name} must override one of the OnExecuteSync methods.");

        protected virtual CommandResponse OnExecuteSync(CommandContext context, Dictionary<string, object> args) =>
            throw new NotImplementedException($"Command {GetType().Name} must override one of the OnExecuteSync methods.");

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (_permissionAttribute != null && !sender.CheckPermission(_permissionAttribute.Permission))
            {
                response = $"You do not have permission to use this command. Required: {_permissionAttribute.Permission}";
                return false;
            }

            var context = new CommandContext(sender, new CommandArguments(arguments));

            if (ArgumentsDefinition != null && ArgumentsDefinition.Any())
            {
                if (!CommandArgPreParser.TryParseArguments(context.Arguments, ArgumentsDefinition, out var parsedArgs, out var errorMessage))
                {
                    response = $"{errorMessage}\nUsage: {Command} {Usage.FirstOrDefault()}";
                    return false;
                }

                try
                {
                    var cmdResponse = OnExecuteSync(context, parsedArgs);
                    response = cmdResponse.Message;
                    return cmdResponse.IsSuccess;
                }
                catch (NotImplementedException)
                {
                    return ExecuteLegacy(context, out response);
                }
                catch (Exception e)
                {
                    Log.Error($"An error occurred while executing command '{Command}':\n{e}");
                    response = "An internal error occurred.";
                    return false;
                }
            }

            return ExecuteLegacy(context, out response);
        }

        private bool ExecuteLegacy(CommandContext context, out string response)
        {
            if (context.Arguments.Count < MinArgs)
            {
                response = "Invalid usage. Usage: " + string.Join("\n", Usage.Select(u => $"{Command} {u}"));
                return false;
            }

            try
            {
                var cmdResponse = OnExecuteSync(context);
                response = cmdResponse.Message;
                return cmdResponse.IsSuccess;
            }
            catch (Exception e)
            {
                Log.Error($"An error occurred while executing command '{Command}':\n{e}");
                response = "An internal error occurred.";
                return false;
            }
        }
    }
}