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

        public bool SanitizeResponse { get; } = false;

        protected SyncCommand()
        {
            _commandAttribute = GetType().GetCustomAttribute<Command>();
            _permissionAttribute = GetType().GetCustomAttribute<CommandPermission>();

            if (_commandAttribute == null)
                throw new ArgumentException($"Command {GetType().Name} is missing the [Command] attribute.");

            Command = _commandAttribute.Name;
            Aliases = _commandAttribute.Aliases;
            Description = _commandAttribute.Description;
            
            if (ArgumentsDefinition != null && ArgumentsDefinition.Any())
            {
                MinArgs = ArgumentsDefinition.Count(a => !a.IsOptional);
                Usage = new[] { GenerateUsageFromDefinition() };
            }
        }
        protected virtual CommandResponse OnExecuteSync(CommandContext context) =>
            throw new NotImplementedException($"Command {GetType().Name} must override one of the OnExecuteSync methods.");

        protected virtual CommandResponse OnExecuteSync(CommandContext context, object[] args) =>
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
                if (!TryParseArguments(context.Arguments, out var parsedArgs, out var errorMessage))
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
        
        private bool TryParseArguments(CommandArguments rawArgs, out object[] parsedArgs, out string errorMessage)
        {
            var results = new List<object>();
            parsedArgs = null;
            errorMessage = string.Empty;
            
            if (rawArgs.Count < MinArgs)
            {
                errorMessage = "Not enough arguments provided.";
                return false;
            }
            
            for (int i = 0; i < ArgumentsDefinition.Count; i++)
            {
                var definition = ArgumentsDefinition[i];
                
                if (definition.IsNeedManyWords)
                {
                    if (definition.Type != typeof(string) || i != ArgumentsDefinition.Count - 1)
                    {
                        errorMessage = $"Internal command error: Greedy argument '{definition.Name}' is misconfigured.";
                        return false;
                    }

                    if (rawArgs.Count > i)
                        results.Add(rawArgs.Join(i));
                    else if (!definition.IsOptional)
                    {
                        errorMessage = $"Missing required argument '{definition.Name}'.";
                        return false;
                    }
                    else
                        results.Add(default(string));
                    break;
                }

                string currentArg = rawArgs[i];
                if (currentArg == null)
                {
                    if (definition.IsOptional)
                    {
                        results.Add(definition.Type.IsValueType ? Activator.CreateInstance(definition.Type) : null);
                        continue;
                    }
                    errorMessage = $"Missing required argument '{definition.Name}'.";
                    return false;
                }

                if (!ArgumentParser.TryParse(currentArg, definition.Type, out var parsedValue, out var parseError))
                {
                    errorMessage = $"Invalid value for argument '{definition.Name}': {parseError}";
                    return false;
                }
                results.Add(parsedValue);
            }

            parsedArgs = results.ToArray();
            return true;
        }

        private string GenerateUsageFromDefinition()
        {
            var sb = new StringBuilder();
            foreach (var definition in ArgumentsDefinition)
            {
                if (definition.IsOptional)
                    sb.Append($"[{definition.Name}] ");
                else if(definition.IsNeedManyWords)
                    sb.Append($"<{definition.Name}...> ");
                else
                    sb.Append($"<{definition.Name}> ");
            }
            return sb.ToString().Trim();
        }
    }
}