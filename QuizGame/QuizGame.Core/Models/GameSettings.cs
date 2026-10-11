using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuizGame.QuizGame.Core
{
    public class GameSettings
    {
        // Сколько секунд играет отрывок
        public int PlayDurationSec { get; set; } = 20;

        // С какого места начинать (сек). Если UseRandomStart=true — игнорируется
        public int StartAtSec { get; set; } = 0;
        public bool UseRandomStart { get; set; } = true;

        // Обратный отсчёт перед началом трека
        public bool UseStartCountdown { get; set; } = true;
        public int StartCountdownSec { get; set; } = 3;
        public string StartCountdownSoundPath { get; set; } = "";

        // Звук начала нового раунда
        public bool PlayRoundStartSound { get; set; } = false;
        public string RoundStartSoundPath { get; set; } = "";

        // Таймер ответа
        public int AnswerTimeSec { get; set; } = 10;

        // Очки и звуки для кнопок 2 / 1 / 0 / -1
        public List<ScoreButton> ScoreButtons { get; set; } = new List<ScoreButton>
    {
        new ScoreButton { Score =  2, SoundPath = "" },
        new ScoreButton { Score =  1, SoundPath = "" },
        new ScoreButton { Score =  0, SoundPath = "" },
        new ScoreButton { Score = -1, SoundPath = "" },
    };

        // Автопауза музыки при нажатии кнопки игроком
        public bool PauseMusicOnPress { get; set; } = true;

        // Прочее
        public bool AllowKeyboardCategorySelect { get; set; } = true; // 1-9
        public int MinTracksPerCategory { get; set; } = 1; // меньше — категория считается пустой
    }

    public class ScoreButton
    {
        public int Score { get; set; }
        public string SoundPath { get; set; } = "";
    }
}
