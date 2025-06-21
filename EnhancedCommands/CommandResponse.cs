namespace EnhancedCommands
{
    public struct CommandResponse
    {
        public string Message { get; }
        public bool IsSuccess { get; }

        public CommandResponse(string message, bool isSuccess)
        {
            Message = message;
            IsSuccess = isSuccess;
        }
        
        public static CommandResponse Ok(string message) => new CommandResponse(message, true);
        public static CommandResponse Fail(string message) => new CommandResponse(message, false);
    }
}