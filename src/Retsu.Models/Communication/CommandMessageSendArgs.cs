namespace Retsu.Models.Communication
{
    public class CommandMessageSendArgs
    {
        public CommandMessage Message { get; set; }
        public string EventName { get; set; }
    }
}