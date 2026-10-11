using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GuessMelody.Core.Buttons
{
    public class ButtonBinding
    {
        public int TeamId { get; set; }
        public string TeamName { get; set; } = "";
        public string Mac { get; set; } = "";
    }

    public class ButtonsConfig
    {
        public List<ButtonBinding> Bindings { get; set; } = new List<ButtonBinding>();
    }


}
