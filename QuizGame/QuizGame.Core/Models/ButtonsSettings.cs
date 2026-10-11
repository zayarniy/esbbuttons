using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuizGame.QuizGame.Core
{
    public class ButtonsSettings
    {
        public List<PlayerButton> Players { get; set; } = new List<PlayerButton>();

        public int ListenPort { get; set; } = 41234;
        public int HeartbeatTimeoutSec { get; set; } = 30;
    }

    public class PlayerButton
    {
        public string Mac { get; set; } = "";        // привязка к MAC
        public string DisplayName { get; set; } = ""; // имя игрока
        public string Color { get; set; } = "#FF6666"; // цвет на экране игрока
        public int SeatIndex { get; set; } = 0;       // порядок в окне
    }
}

