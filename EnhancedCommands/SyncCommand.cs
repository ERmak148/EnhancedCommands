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
            
            if (ArgumentsDefinition != null && ArgumentsDefinition.Any())
            {
                MinArgs = ArgumentsDefinition.Count(a => !a.IsOptional);
                Usage = new[] { GenerateUsageFromDefinition() };
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
        
        private bool TryParseArguments(CommandArguments rawArgs, out Dictionary<string, object> parsedArgs, out string errorMessage)
        {
            parsedArgs = new Dictionary<string, object>();
            HashSet<string> usedDefinitions = new HashSet<string>();
            errorMessage = string.Empty;
            
            foreach (ArgumentDefinition definition in ArgumentsDefinition)
            {
                if (definition.IsOptional)
                    parsedArgs[definition.Name] = definition.Type.IsValueType ? Activator.CreateInstance(definition.Type) : null;
            }

            int argIndex = 0;
            for (int i = 0; i < rawArgs.Count; i++)
            {
                string currentArg = rawArgs[i];
                string argName = null;
                string argValue = currentArg;

                if (currentArg.Contains(":"))
                {
                    var parts = currentArg.Split(new[] { ':' }, 2);
                    if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                    {
                        errorMessage = $"Invalid named argument format: '{currentArg}'. Use 'name:value'.";
                        return false;
                    }
                    argName = parts[0].Trim();
                    argValue = parts[1].Trim();
                }

                ArgumentDefinition definition;
                if (argName != null)
                {
                    definition = ArgumentsDefinition.FirstOrDefault(d => d.Name.Equals(argName, StringComparison.OrdinalIgnoreCase));
                    if (definition == null || !definition.IsNamed)
                    {
                        errorMessage = $"Unknown or non-named argument '{argName}'.";
                        return false;
                    }
                }
                else
                {
                    definition = ArgumentsDefinition
                        .Where(d => !usedDefinitions.Contains(d.Name) && (!d.IsNeedManyWords || argIndex == ArgumentsDefinition.Count - 1))
                        .ElementAtOrDefault(argIndex);
                    if (definition == null)
                    {
                        errorMessage = $"Too many positional arguments provided at position {argIndex + 1}.";
                        return false;
                    }
                    argIndex++;
                }

                if (definition.IsNeedManyWords)
                {
                    if (definition.Type != typeof(string))
                    {
                        errorMessage = $"Greedy argument '{definition.Name}' must be of type string.";
                        return false;
                    }
                    if (argName != null)
                    {
                        var remaining = string.Join(" ", rawArgs.Skip(i));
                        parsedArgs[definition.Name] = remaining;
                        i = rawArgs.Count;
                    }
                    else
                    {
                        if (argIndex != ArgumentsDefinition.Count)
                        {
                            errorMessage = $"Greedy argument '{definition.Name}' must be the last argument.";
                            return false;
                        }
                        parsedArgs[definition.Name] = string.Join(" ", rawArgs.Skip(i));
                        i = rawArgs.Count;
                    }
                }
                else
                {
                    if (!ArgumentParser.TryParse(argValue, definition.Type, out var parsedValue, out var parseError, definition.Constructor))
                    {
                        errorMessage = $"Invalid value for argument '{definition.Name}': {parseError}";
                        return false;
                    }
                    parsedArgs[definition.Name] = parsedValue;
                }

                usedDefinitions.Add(definition.Name);
            }
            
            foreach (var definition in ArgumentsDefinition)
            {
                if (!definition.IsOptional && !usedDefinitions.Contains(definition.Name))
                {
                    errorMessage = $"Missing required argument '{definition.Name}'.";
                    return false;
                }
            }

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