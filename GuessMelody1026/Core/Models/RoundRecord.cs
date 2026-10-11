using System;

namespace GuessMelody.Core.Models
{
    public class RoundRecord
    {
        public int RoundNumber { get; set; }
        public string Category { get; set; }
        public string TrackFile { get; set; }
        public string WinnerMac { get; set; }
        public string WinnerName { get; set; }
        public int Score { get; set; }          // ← ДОБАВЛЕНО
        public EndReason Reason { get; set; }
        public double AnswerTimeSec { get; set; }          // ← ДОБАВЛЕНО

        public DateTime StartedUtc { get; set; }
        public DateTime FinishedUtc { get; set; }

        // Удобные производные для UI
        public string StartedLocal => StartedUtc.ToLocalTime().ToString("HH:mm:ss");
        public string FinishedLocal => FinishedUtc.ToLocalTime().ToString("HH:mm:ss");
        public string ScoreText => Score == 0 ? "0" : (Score > 0 ? $"+{Score}" : Score.ToString());
    }
}