using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using CommandSystem;
using Exiled.API.Features;
using Exiled.Permissions.Extensions;
using MEC;

namespace EnhancedCommands
{
    public abstract class AsyncCommand : ICommand, IUsageProvider
    {
        private enum OverrideType
        {
            None,
            Legacy, // OnExecuteAsync(context, onDone)
            Modern  // OnExecuteAsync(context, args, onDone)
        }
        
        private readonly Command _commandAttribute;
        private readonly CommandPermission _permissionAttribute;
        private readonly OverrideType _implementedOverride;

        public string Command { get; }
        public string[] Aliases { get; }
        public string Description { get; }

        public virtual int MinArgs { get; protected set; }
        public virtual string[] Usage { get; protected set; }
        
        public virtual IReadOnlyList<ArgumentDefinition> ArgumentsDefinition { get; } = null;

        public bool SanitizeResponse { get; } = false;

        protected AsyncCommand()
        {
            Type type = GetType();
            _commandAttribute = type.GetCustomAttribute<Command>();
            _permissionAttribute = type.GetCustomAttribute<CommandPermission>();

            if (_commandAttribute == null)
                throw new ArgumentException($"Command {type.Name} is missing the [Command] attribute.");

            Command = _commandAttribute.Name;
            Aliases = _commandAttribute.Aliases;
            Description = _commandAttribute.Description;

            if (ArgumentsDefinition != null && ArgumentsDefinition.Any())
            {
                MinArgs = ArgumentsDefinition.Count(a => !a.IsOptional);
                Usage = new[] { GenerateUsageFromDefinition() };
            }
            var modernMethod = type.GetMethod(nameof(OnExecuteAsync), BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(CommandContext), typeof(object[]), typeof(Action<CommandResponse>) }, null);
            var legacyMethod = type.GetMethod(nameof(OnExecuteAsync), BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(CommandContext), typeof(Action<CommandResponse>) }, null);

            if (modernMethod != null && modernMethod.DeclaringType == type)
                _implementedOverride = OverrideType.Modern;
            else if (legacyMethod != null && legacyMethod.DeclaringType == type)
                _implementedOverride = OverrideType.Legacy;
            else
                _implementedOverride = OverrideType.None;
        }
        
        protected virtual IEnumerator<float> OnExecuteAsync(CommandContext context, Action<CommandResponse> onDone) => null;
        protected virtual IEnumerator<float> OnExecuteAsync(CommandContext context, object[] args, Action<CommandResponse> onDone) => null;

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (_permissionAttribute != null && !sender.CheckPermission(_permissionAttribute.Permission))
            {
                response = $"You do not have permission to use this command. Required: {_permissionAttribute.Permission}";
                return false;
            }

            if (_implementedOverride == OverrideType.None)
            {
                response = $"Internal command error: Command '{Command}' does not implement an execution method.";
                Log.Error(response);
                return false;
            }
            
            var context = new CommandContext(sender, new CommandArguments(arguments));
            
            if (_implementedOverride == OverrideType.Modern)
            {
                if (ArgumentsDefinition == null || !ArgumentsDefinition.Any())
                {
                     response = $"Internal command error: Command '{Command}' implements the modern execution method but provides no ArgumentDefinition.";
                     Log.Error(response);
                     return false;
                }
                
                if (!TryParseArguments(context.Arguments, out var parsedArgs, out var errorMessage))
                {
                    response = $"{errorMessage}\nUsage: {Command} {Usage.FirstOrDefault()}";
                    return false;
                }
                
                Timing.RunCoroutine(ExecutionCoroutine(context, parsedArgs));
                response = "Command execution started...";
                return true;
            }
            
            if (_implementedOverride == OverrideType.Legacy)
            {
                if (context.Arguments.Count < MinArgs)
                { 
                    response = "Invalid usage. Usage: " + string.Join("\n", Usage.Select(u => $"{Command} {u}"));
                    return false;
                }
                Timing.RunCoroutine(ExecutionCoroutine(context));
                response = "Command execution started...";
                return true;
            }
            response = "Could not execute command due to an internal configuration error.";
            return false;
        }
        
        private IEnumerator<float> ExecutionCoroutine(CommandContext context, object[] parsedArgs)
        {
            CommandResponse finalResponse = default;
            Action<CommandResponse> onDone = resp => finalResponse = resp;
            
            var coroutine = OnExecuteAsync(context, parsedArgs, onDone);
            
            if (coroutine != null)
                yield return Timing.WaitUntilDone(coroutine);
            else
                Log.Warn($"Async command '{Command}' returned a null coroutine. This may be unintentional.");

            if (!string.IsNullOrEmpty(finalResponse.Message))
                context.Sender.Respond(finalResponse.Message, finalResponse.IsSuccess);
        }

        private IEnumerator<float> ExecutionCoroutine(CommandContext context)
        {
            CommandResponse finalResponse = default;
            Action<CommandResponse> onDone = resp => finalResponse = resp;

            var coroutine = OnExecuteAsync(context, onDone);

            if (coroutine != null)
                yield return Timing.WaitUntilDone(coroutine);
            else
                Log.Warn($"Async command '{Command}' returned a null coroutine. This may be unintentional.");

            if(!string.IsNullOrEmpty(finalResponse.Message))
                context.Sender.Respond(finalResponse.Message, finalResponse.IsSuccess);
        }
        
        #region Argument Parsing
        private bool TryParseArguments(CommandArguments rawArgs, out object[] parsedArgs, out string errorMessage)
        {
            Dictionary<string, object> results = new Dictionary<string, object>();
            HashSet<string> usedDefinitions = new HashSet<string>();
            parsedArgs = null;
            errorMessage = string.Empty;
            
            foreach (ArgumentDefinition definition in ArgumentsDefinition)
            {
                if (definition.IsOptional)
                    results[definition.Name] = definition.Type.IsValueType ? Activator.CreateInstance(definition.Type) : null;
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
                        results[definition.Name] = remaining;
                        i = rawArgs.Count;
                    }
                    else
                    {
                        if (argIndex != ArgumentsDefinition.Count)
                        {
                            errorMessage = $"Greedy argument '{definition.Name}' must be the last argument.";
                            return false;
                        }
                        results[definition.Name] = string.Join(" ", rawArgs.Skip(i));
                        i = rawArgs.Count;
                    }
                }
                else
                {
                    if (!ArgumentParser.TryParse(argValue, definition.Type, out var parsedValue, out var parseError))
                    {
                        errorMessage = $"Invalid value for argument '{definition.Name}': {parseError}";
                        return false;
                    }
                    results[definition.Name] = parsedValue;
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
            
            parsedArgs = ArgumentsDefinition.Select(d => results.ContainsKey(d.Name) ? results[d.Name] : null).ToArray();
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
        #endregion
    }
}