namespace Relay.Models
{
    public class RelayChannelState
    {
        public byte ChannelNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsOn { get; set; }
        public bool IsAvailable { get; set; } = true;
    }
}