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
        private static bool IsNamedArgument(string argument)
        {
            if (string.IsNullOrWhiteSpace(argument) || argument.Length < 3)
                return false;

            int colonIndex = argument.IndexOf(':');
            return colonIndex > 0 && colonIndex < argument.Length - 1;
        }
        
        private static bool ValidateNoNamedArgumentsAfter(CommandArguments args, int currentIndex)
        {
            for (int j = currentIndex + 1; j < args.Count; j++)
            {
                if (IsNamedArgument(args[j]))
                {
                    return false;
                }
            }
            return true;
        }
        
        private string GetAvailableArgumentNames()
        {
            if (ArgumentsDefinition == null || !ArgumentsDefinition.Any())
                return "none";
    
            return string.Join(", ", ArgumentsDefinition.Select(d => d.Name));
        }
        
        private List<string> ProcessQuotedArguments(CommandArguments rawArgs)
        {
            var result = new List<string>();
            StringBuilder currentQuoted = null;
    
            for (int i = 0; i < rawArgs.Count; i++)
            {
                string arg = rawArgs[i];
        
                if (currentQuoted != null)
                {
                    currentQuoted.Append(" ").Append(arg);
            
                    if (arg.EndsWith("\""))
                    {
                        string fullText = currentQuoted.ToString();
                        result.Add(fullText.Substring(1, fullText.Length - 2));
                        currentQuoted = null;
                    }
                }
                else if (arg.StartsWith("\""))
                {
                    if (arg.EndsWith("\"") && arg.Length > 1)
                    {
                        result.Add(arg.Substring(1, arg.Length - 2));
                    }
                    else
                    {
                        currentQuoted = new StringBuilder(arg);
                    }
                }
                else
                {
                    result.Add(arg);
                }
            }
    
            if (currentQuoted != null)
            {
                result.Add(currentQuoted.ToString());
            }
    
            return result;
        }
        
        private bool TryParseArguments(
            CommandArguments rawArgs, 
            out Dictionary<string, object> parsedArgs, 
            out string errorMessage)
        {
            parsedArgs = new Dictionary<string, object>();
            errorMessage = string.Empty;
            
            List<string> processedArgs = ProcessQuotedArguments(rawArgs);
            
            var usedDefinitions = new HashSet<string>();
            bool namedArgumentEncountered = false;

            for (int i = 0; i < processedArgs.Count; i++)
            {
                string currentArg = processedArgs[i];
                ArgumentDefinition definition;
                string value;
                
                if (IsNamedArgument(currentArg))
                {
                    namedArgumentEncountered = true;

                    var parts = currentArg.Split(new[] { ':' }, 2);
                    
                    if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
                    {
                        errorMessage = $"Invalid named argument format: '{currentArg}'. Expected format: 'name:value'.";
                        return false;
                    }

                    string argName = parts[0].Trim();
                    value = parts[1].Trim();
                    
                    definition = ArgumentsDefinition.FirstOrDefault(d =>
                        d.Name.Equals(argName, StringComparison.OrdinalIgnoreCase));
                    
                    if (definition == null)
                    {
                        errorMessage = $"Unknown argument '{argName}'. Available arguments: {GetAvailableArgumentNames()}.";
                        return false;
                    }
                    
                    if (usedDefinitions.Contains(definition.Name))
                    {
                        errorMessage = $"Argument '{definition.Name}' has been provided more than once.";
                        return false;
                    }
                    
                    if (definition.IsNeedManyWords)
                    {
                        errorMessage = $"Greedy argument '{definition.Name}' cannot be used with named syntax. " +
                                      $"Use positional syntax instead: <{definition.Name}>";
                        return false;
                        
                        /*
                        if (string.IsNullOrWhiteSpace(value))
                        {
                            errorMessage = $"Greedy argument '{definition.Name}' in named syntax must have a value.";
                            return false;
                        }
                        
                        if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length >= 2)
                        {
                            value = value.Substring(1, value.Length - 2);
                        }
                        */
                    }
                    
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        if (definition.Type == typeof(string))
                        {
                            value = string.Empty;
                        }
                        else
                        {
                            errorMessage = $"Argument '{definition.Name}' cannot have an empty value.";
                            return false;
                        }
                    }
                }
                else
                {
                    if (namedArgumentEncountered)
                    {
                        errorMessage = $"Positional arguments are not allowed after named arguments. " +
                                      $"Invalid argument: '{currentArg}'.";
                        return false;
                    }
                    
                    definition = ArgumentsDefinition.FirstOrDefault(d => !usedDefinitions.Contains(d.Name));

                    if (definition == null)
                    {
                        errorMessage = $"Too many arguments provided. Unexpected argument: '{currentArg}'. " +
                                      $"Expected {ArgumentsDefinition.Count} argument(s).";
                        return false;
                    }

                    value = currentArg;
                    
                    if (definition.IsNeedManyWords)
                    {
                        if (definition.Type != typeof(string))
                        {
                            errorMessage = $"Greedy argument '{definition.Name}' must be of type string, " +
                                          $"but is defined as {definition.Type.Name}.";
                            return false;
                        }
                        var remainingDefinitions = ArgumentsDefinition
                            .Where(d => !usedDefinitions.Contains(d.Name))
                            .ToList();
                        
                        if (remainingDefinitions.Count > 1)
                        {
                            var nextDef = remainingDefinitions[1];
                            errorMessage = $"Greedy argument '{definition.Name}' must be the last argument. " +
                                          $"Found '{nextDef.Name}' after it.";
                            return false;
                        }
                        
                        if (!ValidateNoNamedArgumentsAfter(rawArgs, i))
                        {
                            errorMessage = $"Greedy argument '{definition.Name}' cannot be followed by named arguments. " +
                                          $"All remaining tokens will be consumed.";
                            return false;
                        }
                        
                        value = HandleGreedyValue(value, rawArgs, ref i);
                    }
                }
                
                if (!ArgumentParser.TryParse(value, definition.Type, out var parsedValue, out var parseError, definition.Constructor))
                {
                    errorMessage = $"Invalid value for argument '{definition.Name}': {parseError}";
                    return false;
                }

                parsedArgs[definition.Name] = parsedValue;
                usedDefinitions.Add(definition.Name);
            }
            
            foreach (var def in ArgumentsDefinition)
            {
                if (!def.IsOptional && !usedDefinitions.Contains(def.Name))
                {
                    errorMessage = $"Missing required argument '{def.Name}' ({def.Type.Name}).";
                    return false;
                }
            }

            return true;
        }

        private static string HandleGreedyValue(string initialValue, CommandArguments rawArgs, ref int currentIndex)
        {
            StringBuilder sb = new StringBuilder(initialValue);
            
            bool startsWithQuote = initialValue.StartsWith("\"");
            bool endsWithQuote = initialValue.EndsWith("\"");
            bool isQuoted = startsWithQuote && !endsWithQuote;
    
            if (isQuoted)
            {
                bool foundClosingQuote = false;
        
                for (int j = currentIndex + 1; j < rawArgs.Count; j++)
                {
                    sb.Append(" ").Append(rawArgs[j]);
            
                    if (rawArgs[j].EndsWith("\""))
                    {
                        currentIndex = j;
                        foundClosingQuote = true;
                        break;
                    }
                }
        
                string quotedValue = sb.ToString();
                if (foundClosingQuote && 
                    quotedValue.Length >= 2 && 
                    quotedValue.StartsWith("\"") && 
                    quotedValue.EndsWith("\""))
                {
                    return quotedValue.Substring(1, quotedValue.Length - 2);
                }
                return quotedValue;
            }
            if (startsWithQuote && endsWithQuote && initialValue.Length >= 2)
            {
                return initialValue.Substring(1, initialValue.Length - 2);
            }
            for (int j = currentIndex + 1; j < rawArgs.Count; j++)
            {
                sb.Append(" ").Append(rawArgs[j]);
            }
    
            currentIndex = rawArgs.Count - 1;
            return sb.ToString();
        }
        private string GenerateUsageFromDefinition()
        {
            var sb = new StringBuilder();
            foreach (var definition in ArgumentsDefinition)
            {
                if (definition.IsOptional)
                    sb.Append($"[{definition.Name} ({definition.Type.Name})] ");
                else if(definition.IsNeedManyWords)
                    sb.Append($"<{definition.Name} ({definition.Type.Name})...> ");
                else
                    sb.Append($"<{definition.Name} ({definition.Type.Name})> ");
            }
            return sb.ToString().Trim();
        }
    }
}