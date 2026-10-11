using System;
using System.Collections.Generic;

namespace GuessMelody.AnswerServer
{
    /// <summary>Снимок состояния игры для веб-страницы.</summary>
    public class GameStateSnapshot
    {
        public string Title { get; set; } = "Угадай мелодию";
        public string GameName { get; set; } = "";
        public string RoundText { get; set; } = "";
        public string StateText { get; set; } = "";
        public string CategoryText { get; set; } = "";
        public string TrackText { get; set; } = "";
        public string FirstPressedText { get; set; } = "";
        public string PlaceholderText { get; set; } = "";

        public double TrackLeftSec { get; set; }
        public double TrackTotalSec { get; set; }
        public double AnswerLeftSec { get; set; }

        public List<TeamScore> Teams { get; set; } = new List<TeamScore>();
        public List<CategoryInfo> Categories { get; set; } = new List<CategoryInfo>();

        public DateTime ServerTime { get; set; } = DateTime.Now;

        /// <summary>Ревизия — увеличивается при каждом изменении. Фронт сравнивает, чтобы не перерисовывать зря.</summary>
        public long Revision { get; set; }
    }

    public class TeamScore
    {
        public string Name { get; set; }
        public int Score { get; set; }
        public bool FirstPressed { get; set; }
    }

    public class CategoryInfo
    {
        public string Name { get; set; }
        public int Total { get; set; }
        public int Remaining { get; set; }
        public bool IsActive { get; set; }
    }
}