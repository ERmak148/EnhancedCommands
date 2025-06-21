using CommandSystem;
using Exiled.API.Features;
using RemoteAdmin;

namespace EnhancedCommands
{
    public class CommandContext
    {
        public ICommandSender Sender { get; }
        
        public Player Player { get; }
        
        public CommandArguments Arguments { get; }
        
        public bool IsPlayer => Sender is PlayerCommandSender;
        
        public bool IsConsole => Sender is ServerConsoleSender;

        public CommandContext(ICommandSender sender, CommandArguments arguments)
        {
            Sender = sender;
            Arguments = arguments;
            if (IsPlayer)
                Player = Player.Get(sender);
            else
                Player = null;
        }
    }
}