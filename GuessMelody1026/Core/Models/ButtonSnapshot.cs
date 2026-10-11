using System;

namespace GuessMelody.Core.Models
{
    public class ButtonSnapshot
    {
        public string Mac { get; set; }
        public string DisplayName { get; set; }
        public int? PlayerSlot { get; set; }

        public string LastIp { get; set; }
        public bool Alive { get; set; }
        public DateTime FirstSeenUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }

        public int PressCount { get; set; }
        public string LastSeq { get; set; }
        public long LastUptimeMs { get; set; }

        public uint HbReceived { get; set; }
        public uint HbExpected { get; set; }
        public double DeliveryPercent { get; set; }

        public double SecondsSinceLastSeen =>
            (DateTime.UtcNow - LastSeenUtc).TotalSeconds;

        public string Label =>
            string.IsNullOrWhiteSpace(DisplayName) ? Mac : DisplayName;
    }
}