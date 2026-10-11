using System.Collections.Generic;

namespace GuessMelody.Core.Models
{
    public class ButtonBinding
    {
        public string Mac { get; set; }
        public string DisplayName { get; set; }
        public int? PlayerSlot { get; set; }   // 1..N, опционально
    }

    public class ButtonBindings
    {
        public int SchemaVersion { get; set; } = 1;
        public List<ButtonBinding> Bindings { get; set; } = new List<ButtonBinding>();
    }
}