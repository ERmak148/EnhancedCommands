using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace EnhancedCommands;

public static class CommandArgPreParser
{
    private static bool IsNamedArgument(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument) || argument.Length < 3)
            return false;

        bool inQuotes = false;
        for (int i = 0; i < argument.Length; i++)
        {
            if (argument[i] == '"' && !IsEscaped(argument, i))
            {
                inQuotes = !inQuotes;
            }
            else if (argument[i] == ':' && !inQuotes)
            {
                return i > 0 && i < argument.Length - 1;
            }
        }
        return false;
    }

    private static bool IsEscaped(string str, int index)
    {
        if (index == 0) return false;
        int backslashCount = 0;
        for (int i = index - 1; i >= 0 && str[i] == '\\'; i--)
        {
            backslashCount++;
        }
        return backslashCount % 2 == 1;
    }

    private static string GetAvailableArgumentNames(IReadOnlyList<ArgumentDefinition> definitions)
    {
        if (definitions == null || definitions.Count == 0)
            return "none";

        return string.Join(", ", definitions.Select(d => d.Name));
    }

    private static List<string> ProcessQuotedArguments(CommandArguments rawArgs)
    {
        var result = new List<string>();
        StringBuilder currentQuoted = null;

        for (int i = 0; i < rawArgs.Count; i++)
        {
            string arg = rawArgs[i];

            if (currentQuoted != null)
            {
                currentQuoted.Append(" ").Append(arg);

                if (arg.EndsWith("\"") && !IsEscaped(arg, arg.Length - 1))
                {
                    string fullText = currentQuoted.ToString();
                    result.Add(UnquoteString(fullText));
                    currentQuoted = null;
                }
            }
            else if (arg.StartsWith("\"") && !IsEscaped(arg, 0))
            {
                if (arg.Length > 1 && arg.EndsWith("\"") && !IsEscaped(arg, arg.Length - 1))
                {
                    result.Add(UnquoteString(arg));
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
            throw new ArgumentException("Unclosed quotation mark in arguments.");
        }

        return result;
    }

    private static string UnquoteString(string str)
    {
        if (string.IsNullOrEmpty(str)) return str;

        if (str.StartsWith("\""))
        {
            if (str.Length < 2)
            {
                throw new ArgumentException($"Invalid quoted string: {str}");
            }

            if (str.EndsWith("\"") && !IsEscaped(str, str.Length - 1))
            {
                return str.Substring(1, str.Length - 2);
            }

            throw new ArgumentException($"Unclosed quotation mark: {str}");
        }

        return str;
    }

    public static bool TryParseArguments(
        CommandArguments rawArgs,
        IReadOnlyList<ArgumentDefinition> argumentsDefinition,
        out Dictionary<string, object> parsedArgs,
        out string errorMessage)
    {
        parsedArgs = new Dictionary<string, object>();
        errorMessage = string.Empty;

        if (argumentsDefinition == null || argumentsDefinition.Count == 0)
        {
            return true;
        }

        List<string> processedArgs;
        try
        {
            processedArgs = ProcessQuotedArguments(rawArgs);
        }
        catch (ArgumentException ex)
        {
            errorMessage = ex.Message;
            return false;
        }

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

                var parts = SplitNamedArgument(currentArg);

                if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
                {
                    errorMessage = $"Invalid named argument format: '{currentArg}'. Expected format: 'name:value'.";
                    return false;
                }

                string argName = parts[0].Trim();
                value = parts[1];

                definition = argumentsDefinition.FirstOrDefault(d =>
                    d.Name.Equals(argName, StringComparison.OrdinalIgnoreCase));

                if (definition == null)
                {
                    errorMessage = $"Unknown argument '{argName}'. Available arguments: {GetAvailableArgumentNames(argumentsDefinition)}.";
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

                definition = argumentsDefinition.FirstOrDefault(d => !usedDefinitions.Contains(d.Name));

                if (definition == null)
                {
                    errorMessage = $"Too many arguments provided. Unexpected argument: '{currentArg}'. " +
                                  $"Expected {argumentsDefinition.Count} argument(s).";
                    return false;
                }

                value = currentArg;

                if (definition.IsNeedManyWords)
                {
                    var remainingDefinitions = argumentsDefinition
                        .Where(d => !usedDefinitions.Contains(d.Name))
                        .ToList();

                    if (remainingDefinitions.Count > 1)
                    {
                        var nextDef = remainingDefinitions[1];
                        errorMessage = $"Greedy argument '{definition.Name}' must be the last argument. " +
                                      $"Found '{nextDef.Name}' after it.";
                        return false;
                    }

                    if (!ValidateNoNamedArgumentsAfter(processedArgs, i))
                    {
                        errorMessage = $"Greedy argument '{definition.Name}' cannot be followed by named arguments.";
                        return false;
                    }

                    value = HandleGreedyValue(value, processedArgs, ref i);
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

        foreach (var def in argumentsDefinition)
        {
            if (!usedDefinitions.Contains(def.Name))
            {
                if (!def.IsOptional)
                {
                    errorMessage = $"Missing required argument '{def.Name}' ({def.Type.Name}).";
                    return false;
                }

                parsedArgs[def.Name] = GetDefaultValue(def.Type);
            }
        }

        return true;
    }

    private static object GetDefaultValue(Type type)
    {
        if (type == typeof(string))
        {
            return string.Empty;
        }

        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }

        return null;
    }

    private static string[] SplitNamedArgument(string argument)
    {
        bool inQuotes = false;
        for (int i = 0; i < argument.Length; i++)
        {
            if (argument[i] == '"' && !IsEscaped(argument, i))
            {
                inQuotes = !inQuotes;
            }
            else if (argument[i] == ':' && !inQuotes)
            {
                return new[] { argument.Substring(0, i), argument.Substring(i + 1) };
            }
        }
        return new[] { argument };
    }

    private static bool ValidateNoNamedArgumentsAfter(List<string> processedArgs, int currentIndex)
    {
        for (int j = currentIndex + 1; j < processedArgs.Count; j++)
        {
            if (IsNamedArgument(processedArgs[j]))
            {
                return false;
            }
        }
        return true;
    }

    private static string HandleGreedyValue(string initialValue, List<string> processedArgs, ref int currentIndex)
    {
        StringBuilder sb = new StringBuilder(initialValue);

        for (int j = currentIndex + 1; j < processedArgs.Count; j++)
        {
            sb.Append(" ").Append(processedArgs[j]);
        }

        currentIndex = processedArgs.Count - 1;
        return sb.ToString();
    }

    public static string GenerateUsageFromDefinition(IReadOnlyList<ArgumentDefinition> argumentsDefinition)
    {
        var sb = new StringBuilder();
        foreach (var definition in argumentsDefinition)
        {
            if (definition.IsOptional)
                sb.Append($"[{definition.Name} ({definition.Type.Name})] ");
            else if (definition.IsNeedManyWords)
                sb.Append($"<{definition.Name} ({definition.Type.Name})...> ");
            else
                sb.Append($"<{definition.Name} ({definition.Type.Name})> ");
        }

        return sb.ToString().Trim();
    }

    public static void ValidateArgumentsDefinition(IReadOnlyList<ArgumentDefinition> argumentsDefinition, string commandName)
    {
        if (argumentsDefinition == null || argumentsDefinition.Count == 0)
            return;

        var names = new HashSet<string>();
        bool greedyFound = false;

        for (int i = 0; i < argumentsDefinition.Count; i++)
        {
            var def = argumentsDefinition[i];

            if (string.IsNullOrWhiteSpace(def.Name))
            {
                throw new ArgumentException($"Command '{commandName}': Argument at index {i} has an empty name.");
            }

            if (!names.Add(def.Name))
            {
                throw new ArgumentException($"Command '{commandName}': Duplicate argument name '{def.Name}'.");
            }

            if (def.IsNeedManyWords)
            {
                if (def.Type != typeof(string))
                {
                    throw new ArgumentException($"Command '{commandName}': Greedy argument '{def.Name}' must be of type string, but is defined as {def.Type.Name}.");
                }

                if (i != argumentsDefinition.Count - 1)
                {
                    throw new ArgumentException($"Command '{commandName}': Greedy argument '{def.Name}' must be the last argument.");
                }

                greedyFound = true;
            }

            if (greedyFound && i < argumentsDefinition.Count - 1)
            {
                throw new ArgumentException($"Command '{commandName}': Cannot have arguments after greedy argument.");
            }
        }
    }
}