# EnhancedCommands: A Modern Command Framework for Exiled

EnhancedCommands is a micro-library designed to dramatically simplify and streamline the creation of server and RemoteAdmin commands within the Exiled framework for *SCP: Secret Laboratory*. It replaces cumbersome, boilerplate-heavy command logic with a modern, attribute-based, and type-safe system.

This framework handles the most tedious parts of command creation—permission checks, argument parsing, type conversion, usage string generation, and sub-command routing—allowing you to focus purely on the logic of your commands.

## Key Features

- **Declarative, Attribute-Based Syntax**: Define commands, aliases, descriptions, and permissions using simple C# attributes (`[Command]`, `[CommandPermission]`).
- **Powerful Argument Parsing**: Automatically parse and validate arguments into native C# types like `Player`, `int`, `float`, `bool`, `enum`, and `string`.
- **Type-Safe Execution**: Receive strongly-typed arguments directly in your execution method, eliminating manual parsing and casting.
- **Synchronous & Asynchronous Commands**: Choose between simple synchronous commands (`SyncCommand`) for quick tasks and coroutine-based asynchronous commands (`AsyncCommand`) for long-running operations without freezing the server.
- **Hierarchical Command Structure**: Easily create parent commands that dispatch to multiple sub-commands (e.g., `myplugin <subcommand> <args>`).
- **Automatic Usage Generation**: The framework automatically generates a `Usage` string based on your `ArgumentDefinition`, which is shown to the user on incorrect input.
- **Optional & "Greedy" Arguments**: Define arguments as optional or as "greedy" arguments that consume the rest of the input (perfect for messages or reasons).
- **Dual Execution Models**: Choose between modern, automatic argument parsing or a legacy, manual parsing model for full control.

---

## Core Concepts

The framework is built around three base classes for commands:

- **`SyncCommand`**: The base class for standard, synchronous commands. The command logic is executed immediately and returns a result. Ideal for most commands like setting health, giving items, or teleporting players.

- **`AsyncCommand`**: The base class for asynchronous commands that can perform long-running tasks (e.g., timed delays, database queries, web requests) without blocking the server thread. It leverages `MEC.Timing` coroutines.

- **`ParentCommandBase`**: A special base class used to create a "parent" command that acts as a router for a collection of sub-commands. It doesn't have its own logic but instead delegates execution to a registered `SyncCommand` or `AsyncCommand`.

---

## Getting Started: How to Create Commands

Below are examples demonstrating how to create different types of commands.

## 1. A Simple Synchronous Command

There are two ways to write a command: the **modern, automatic parsing method** (recommended) and the **legacy, manual parsing method**.

#### Method 1: Automatic Parsing (Recommended)

Let's create a `slay` command that kills a target player.

```csharp
using System.Collections.Generic;
using Exiled.API.Features;
using EnhancedCommands;

// Register the command handler for Remote Admin
[CommandHandler(typeof(RemoteAdminCommandHandler))]
[Command("slay", description: "Kills the specified player.")]
[CommandPermission("myplugin.slay")]
public class SlayCommand : SyncCommand
{
    // Define the arguments the command expects.
    // The framework will parse them in this order.
    public override IReadOnlyList<ArgumentDefinition> ArgumentsDefinition { get; } = new List<ArgumentDefinition>
    {
        new ArgumentDefinition("target", typeof(Player)),
    };

    // This method is called after the arguments have been successfully parsed.
    // 'args' is an object array containing the parsed values.
    protected override CommandResponse OnExecuteSync(CommandContext context, object[] args)
    {
        // Cast the parsed arguments to their expected types.
        var target = (Player)args[0];

        // Execute the command logic.
        target.Kill("Was slayed by an admin.");

        // Return a response to the command sender.
        // CommandResponse.Ok() indicates success.
        // CommandResponse.Fail() indicates failure.
        return CommandResponse.Ok($"{target.Nickname} has been slayed.");
    }
}
```
### How it works:

`[CommandHandler]`: Registers the command with Exiled's RemoteAdminCommandHandler.

`[Command]`: Defines the command's name (slay) and description.

`[CommandPermission]`: Automatically checks if the sender has the myplugin.slay permission.

`ArgumentsDefinition`: We declare one required argument named "target" of type Player. The framework handles all parsing and error feedback.

`OnExecuteSync(context, args)`: This override is only called if all required arguments are provided and parsed successfully. args[0] will contain the Player object.

### In-Game Usage:
```
> /slay 52
< [SUCCESS] SomePlayer123 has been slayed.

> /slay
< [FAILURE] Not enough arguments provided.
< Usage: slay <target>
```
### Method 2: Manual Parsing (Legacy Style)
For simpler commands or for full control over parsing, you can opt-out of the ArgumentsDefinition system and parse arguments manually.

```csharp
using Exiled.API.Features;
using EnhancedCommands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[Command("slay-legacy", description: "Kills a player (legacy implementation).")]
[CommandPermission("myplugin.slay")]
public class SlayCommandLegacy : SyncCommand
{
    // We don't use ArgumentsDefinition.
    // Instead, we manually set MinArgs and Usage.
    public SlayCommandLegacy()
    {
        MinArgs = 1;
        Usage = new[] { "<target_player>" };
    }

    // Override the OnExecuteSync that takes only the context.
    protected override CommandResponse OnExecuteSync(CommandContext context)
    {
        // Manually parse the argument from the context.
        // The CommandArguments helper class provides TryGet... methods.
        if (!context.Arguments.TryGetPlayer(0, out Player target))
        {
            return CommandResponse.Fail("Player not found. Please provide a valid player name, ID, or user ID.");
        }

        // Execute the command logic.
        target.Kill("Was slayed by an admin.");

        // Return a response.
        return CommandResponse.Ok($"{target.Nickname} has been slayed.");
    }
}
```

### How it works:

We do not define ArgumentsDefinition.

We override OnExecuteSync(CommandContext context), which receives the context but no pre-parsed arguments.

We are responsible for checking MinArgs (which the base class does for us) and parsing the arguments from context.Arguments.

The CommandArguments class has helpers like .TryGetPlayer(), .TryGetInt(), etc., to simplify this manual process.

We must manually handle the failure case (e.g., player not found) and return a CommandResponse.Fail.

## 2. An Asynchronous Command with a Delay
   
Let's create a `delayedbroadcast` command that sends a server-wide broadcast after a specified delay. This is a perfect use case for AsyncCommand.

```csharp
using System.Collections.Generic;
using MEC;
using EnhancedCommands;
using Exiled.API.Features;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[Command("delayedbroadcast", aliases: new[] { "db" }, description: "Broadcasts a message after a delay.")]
[CommandPermission("myplugin.broadcast")]
public class DelayedBroadcastCommand : AsyncCommand
{
    public override IReadOnlyList<ArgumentDefinition> ArgumentsDefinition { get; } = new List<ArgumentDefinition>
    {
        new ArgumentDefinition("delay", typeof(float)),
        // 'IsNeedManyWords = true' makes this a "greedy" argument.
        // It must be the last argument in the definition.
        new ArgumentDefinition("message", typeof(string)) { IsNeedManyWords = true },
    };

    // Use the OnExecuteAsync override for async commands.
    protected override IEnumerator<float> OnExecuteAsync(CommandContext context, object[] args, System.Action<CommandResponse> onDone)
    {
        // Cast arguments
        var delay = (float)args[0];
        var message = (string)args[1];

        // Give the sender initial feedback
        context.Sender.Respond($"The broadcast will be sent in {delay} seconds.", true);

        // This is a MEC coroutine yield. It pauses execution without blocking the server.
        yield return Timing.WaitForSeconds(delay);
        
        // After the delay, execute the main logic.
        Map.Broadcast(10, message);

        // Call the onDone callback to send the final response.
        onDone(CommandResponse.Ok("Broadcast sent!"));
    }
}
```

Note: **AsyncCommand** also supports a legacy override: OnExecuteAsync(CommandContext context, Action<CommandResponse> onDone). It works identically to the legacy SyncCommand method, requiring you to parse arguments manually from context.Arguments.

### In-Game Usage:

```
> /db 10 The server will restart soon.
< [EnhancedCommands] The broadcast will be sent in 10 seconds.
(10 seconds later, a broadcast appears for everyone)
< [EnhancedCommands] Broadcast sent!
```

## 3. A Command with an Optional Argument
   
Let's create a `setrole` command that changes a player's role, with an optional argument to also set their health.

```csharp
using System.Collections.Generic;
using Exiled.API.Enums;
using EnhancedCommands;
using Exiled.API.Features;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[Command("setrole", description: "Sets a player's role, with an optional health value.")]
[CommandPermission("myplugin.setrole")]
public class SetRoleCommand : SyncCommand
{
    public override IReadOnlyList<ArgumentDefinition> ArgumentsDefinition { get; } = new List<ArgumentDefinition>
    {
        new ArgumentDefinition("target", typeof(Player)),
        // Enums are automatically parsed (case-insensitive).
        new ArgumentDefinition("role", typeof(RoleType)),
        // 'IsOptional = true' makes this argument optional.
        new ArgumentDefinition("health", typeof(int)) { IsOptional = true },
    };

    protected override CommandResponse OnExecuteSync(CommandContext context, object[] args)
    {
        var target = (Player)args[0];
        var role = (RoleType)args[1];
        // For optional value types, they default to 0 if not provided. For reference types result - null
        var health = (int)args[2];

        target.Role.Set(role);
        
        // If the health argument was provided (it won't be 0 unless the user typed 0), set it.
        if (health > 0)
        {
            target.Health = health;
            return CommandResponse.Ok($"{target.Nickname}'s role set to {role} with {health} HP.");
        }

        return CommandResponse.Ok($"{target.Nickname}'s role set to {role}.");
    }
}
```

### How it works:

RoleType: The parser supports enum types out of the box. It will automatically provide a helpful error if the user provides an invalid role name.

IsOptional = true: This marks the health argument as optional. The command will execute even if it's missing.

Handling Optional Value: When an optional value type like int is not provided, it receives its default value (0). We can check for this to see if the user supplied the argument.


### In-Game Usage:

```
> /setrole PlayerA ClassD
< [SUCCESS] PlayerA's role set to ClassD.

> /setrole 5 Scientist 150
< [SUCCESS] Player with id 5 role set to Scientist with 150 HP.

> /setrole PlayerC InvalidRole
< [FAILURE] Invalid value for argument 'role': Expected one of: Scp173, ClassD, Spectator, Scp106, ...
< Usage: setrole <target> <role> [health]
```

## 4. Creating a Parent Command with Sub-Commands

This is useful for grouping related functionality under a single command, like config <subcommand>.

### Step 1: Create the Sub-Commands First, create your sub-commands as regular SyncCommand or AsyncCommand classes. Do not give them a [CommandHandler] attribute, as the parent will be responsible for registration.
```csharp
[Command("sync", new[] { "s" }, "A synchronous sub-command.")]
[CommandPermission("example.sync")]
public class SubCommandSync : SyncCommand { /* ... logic from previous examples ... */ }

[Command("async", new[] { "a" }, "An asynchronous sub-command.")]
[CommandPermission("example.async")]
public class SubCommandAsync : AsyncCommand { /* ... logic from previous examples ... */ }
```

### Step 2: Create the Parent Command Next, create the parent command that inherits from ParentCommandBase. This class does get the [CommandHandler] attribute.

```csharp
using System.Collections.Generic;
using CommandSystem;
using EnhancedCommands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[Command("parent", new[] { "p" }, "An example parent command.")]
[CommandPermission("example.parent")] // Optional root permission
public class ExampleParentCommand : ParentCommandBase
{
    // Override this property and provide new instances of your sub-commands.
    protected override IReadOnlyList<ICommand> SubCommands { get; } = new List<ICommand>
    {
        new SubCommandSync(),
        new SubCommandAsync()
    };
}
```

### How it works:

The ExampleParentCommand is registered with Exiled.

When .parent sync ... is executed, the ParentCommandBase logic finds that "sync" matches the SubCommandSync instance.

It then forwards the remaining arguments (...) to SubCommandSync for execution.

The parent command automatically handles sub-command routing, permissions, and help message generation.

### In-Game Usage:
```
> /parent
< [FAILURE] An example parent command.
< Usage: parent <subcommand> [arguments...]
< Available subcommands:
<   - sync <target> [amount]: A synchronous sub-command.
<   - async <delay> <message...>: An asynchronous sub-command.

> /parent sync PlayerX 50
< [SUCCESS] Set PlayerX's health to 50.
```

## API Reference Quick-Look
- `ArgumentDefinition(string name, Type type)`
- - `.IsOptional = true`: Makes the argument optional.
- - `.IsNeedManyWords = true`: Makes the argument consume all remaining input. Must be the last argument.

- `CommandContext`
- - `.Sender`: The ICommandSender who ran the command.
- - `.Player`: The Player object of the sender, if they are a player. Null otherwise.
- - `.Arguments`: The CommandArguments wrapper for manual parsing.

- `CommandArguments` (used for manual parsing)
- - `.TryGetPlayer(index, out player)`
- - `.TryGetInt(index, out value)`
- - `.TryGetFloat(index, out value)`
- - `.TryGetBool(index, out value)`
- - `.Join(startIndex)`

- `CommandResponse`
- - `CommandResponse.Ok(string message)`: Creates a success response.
- - `CommandResponse.Fail(string message)`: Creates a failure response.

## Installation
Copy EnhancedCommands.dll into EXILED/Plugins/dependencies directory

Add EnhancedCommands.dll into your project dependencies

Start creating commands

Yeah Im created this readme with gpt cuz i very dumb and do not know english like you, man that reading it right now