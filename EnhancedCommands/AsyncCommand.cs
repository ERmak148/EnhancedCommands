using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
            Legacy,
            Modern
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

            CommandArgPreParser.ValidateArgumentsDefinition(ArgumentsDefinition, Command);

            if (ArgumentsDefinition != null && ArgumentsDefinition.Any())
            {
                MinArgs = ArgumentsDefinition.Count(a => !a.IsOptional);
                Usage = new[] { CommandArgPreParser.GenerateUsageFromDefinition(ArgumentsDefinition) };
            }

            var modernMethod = type.GetMethod(nameof(OnExecuteAsync), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(CommandContext), typeof(Dictionary<string, object>), typeof(Action<CommandResponse>) }, null);
            var legacyMethod = type.GetMethod(nameof(OnExecuteAsync), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(CommandContext), typeof(Action<CommandResponse>) }, null);

            if (modernMethod != null && modernMethod.DeclaringType == type)
                _implementedOverride = OverrideType.Modern;
            else if (legacyMethod != null && legacyMethod.DeclaringType == type)
                _implementedOverride = OverrideType.Legacy;
            else
                _implementedOverride = OverrideType.None;
        }

        protected virtual IEnumerator<float> OnExecuteAsync(CommandContext context, Action<CommandResponse> onDone) =>
            null;

        protected virtual IEnumerator<float> OnExecuteAsync(CommandContext context, Dictionary<string, object> args,
            Action<CommandResponse> onDone) => null;

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (_permissionAttribute != null && !sender.CheckPermission(_permissionAttribute.Permission))
            {
                response =
                    $"You do not have permission to use this command. Required: {_permissionAttribute.Permission}";
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
                    response =
                        $"Internal command error: Command '{Command}' implements the modern execution method but provides no ArgumentDefinition.";
                    Log.Error(response);
                    return false;
                }

                if (!CommandArgPreParser.TryParseArguments(context.Arguments, ArgumentsDefinition, out var parsedArgs, out var errorMessage))
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

        private IEnumerator<float> ExecutionCoroutine(CommandContext context, Dictionary<string, object> parsedArgs)
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

            if (!string.IsNullOrEmpty(finalResponse.Message))
                context.Sender.Respond(finalResponse.Message, finalResponse.IsSuccess);
        }
    }
}