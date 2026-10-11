using System;
using System.Collections.Generic;
using System.Linq;
using GuessMelody.Core;
using GuessMelody.Core.Enums;
using GuessMelody.Core.Models;

namespace GuessMelody.AnswerServer
{
    public class AnswerStatePublisher : IDisposable
    {
        private readonly GameEngine _engine;
        private long _revision;

        public string Title { get; set; } = "Угадай мелодию";
        public string PlaceholderText { get; set; } = "";

        public AnswerStatePublisher(GameEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));

            _engine.StateChanged += (s, e) => Bump();
            _engine.ScoreChanged += (s, e) => Bump();
            _engine.TrackChanged += (s, e) => Bump();
            _engine.RemainingChanged += (s, e) => Bump();
            _engine.CategoryHighlighted += (s, c) => Bump();
            _engine.SettingsChanged += (s, e) => Bump();
        }

        private void Bump() => _revision++;

        public GameStateSnapshot Build()
        {
            var s = _engine.Settings;
            var snap = new GameStateSnapshot
            {
                Title = Title,
                PlaceholderText = PlaceholderText,
                GameName = s?.Name ?? "",
                RoundText = _engine.TotalRounds > 0
                    ? $"Раунд {_engine.CurrentRound + 1} / {_engine.TotalRounds}"
                    : $"Раунд {_engine.CurrentRound + 1}",
                StateText = StateToText(_engine.State),
                CategoryText = _engine.CurrentCategory?.Name ?? "—",
                TrackText = _engine.CurrentTrack?.RelativePath ?? "—",
                FirstPressedText = _engine.FirstPressedTeam?.Name ?? "—",
                TrackLeftSec = _engine.TrackLeftSec,
                TrackTotalSec = _engine.TrackTotalSec,
                AnswerLeftSec = _engine.AnswerLeftSec,
                Revision = _revision
            };

            var firstMac = _engine.FirstPressedTeam?.Mac;

            snap.Teams = _engine.Teams.Select(t => new TeamScore
            {
                Name = t.Name,
                Score = t.Score,
                FirstPressed = !string.IsNullOrEmpty(firstMac) &&
                    string.Equals(t.Mac, firstMac, StringComparison.OrdinalIgnoreCase)
            }).ToList();

            var cats = s?.Folders?.Categories;
            if (cats != null)
            {
                for (int i = 0; i < cats.Count; i++)
                {
                    snap.Categories.Add(new CategoryInfo
                    {
                        Name = cats[i].Name,
                        Total = _engine.GetTotal(i),
                        Remaining = _engine.GetRemaining(i),
                        IsActive = _engine.CurrentCategory != null &&
                            string.Equals(cats[i].Name, _engine.CurrentCategory.Name, StringComparison.OrdinalIgnoreCase)
                    });
                }
            }

            return snap;
        }

        private static string StateToText(RoundState s)
        {
            switch (s)
            {
                case RoundState.Idle: return "Ожидание";
                case RoundState.Countdown: return "Отсчёт";
                case RoundState.Playing: return "Играет";
                case RoundState.WaitingForAnswer: return "Ждём ответ";
                case RoundState.Scored: return "Очко начислено";
                case RoundState.Finished: return "Завершено";
                default: return "";
            }
        }

        public void Dispose() { }
    }
}