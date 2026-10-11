using System;
using System.Windows.Threading;
using GuessMelody.Audio;
using GuessMelody.Core;
using GuessMelody.Core.Enums;

namespace GuessMelody.Services
{
    public class FeedbackService : IDisposable
    {
        private readonly AudioCoordinator _audio;
        private readonly LogService _log;
        private readonly DispatcherTimer _pauseTimer;

        /// <summary>Запрос показать правильный ответ. Строка — уже без расширения.</summary>
        public event EventHandler<string> ShowCorrectAnswer;

        /// <summary>Запрос показать «Неверный ответ».</summary>
        public event EventHandler<string> ShowWrongAnswer;

        /// <summary>Пауза после «Нет» закончилась — можно продолжать музыку.</summary>
        public event EventHandler WrongPauseFinished;

        public FeedbackService(AudioCoordinator audio, LogService log)
        {
            _audio = audio;
            _log = log;

            _pauseTimer = new DispatcherTimer();
            _pauseTimer.Tick += (s, e) =>
            {
                _pauseTimer.Stop();
                _log.Add(LogKind.Audio, "⏱ Пауза после «Нет» завершена");
                WrongPauseFinished?.Invoke(this, EventArgs.Empty);
            };
        }

        /// <summary>Показ правильного ответа — имя файла без расширения.</summary>
        public void ShowCorrect(string trackRelativePath)
        {
            var name = FileNameWithoutExtension(trackRelativePath);
            if (string.IsNullOrEmpty(name)) name = "—";

            // звук «Правильный»
            _audio?.PlayCorrect();

            _log.Add(LogKind.Game, $"✅ Правильный ответ: {name}");
            ShowCorrectAnswer?.Invoke(this, name);
        }

        /// <summary>Показ «Неправильный» + пауза перед продолжением музыки.</summary>
        public void ShowWrong(int pauseSec)
        {
            // звук «Неправильный»
            _audio?.PlayWrong();

            _log.Add(LogKind.Game, $"❌ Неверный ответ (пауза {pauseSec} с)");
            ShowWrongAnswer?.Invoke(this, "❌ Неверный ответ");

            if (pauseSec <= 0)
            {
                WrongPauseFinished?.Invoke(this, EventArgs.Empty);
                return;
            }

            _pauseTimer.Stop();
            _pauseTimer.Interval = TimeSpan.FromSeconds(pauseSec);
            _pauseTimer.Start();
        }

        public void CancelPause()
        {
            _pauseTimer?.Stop();
        }

        private static string FileNameWithoutExtension(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try { return System.IO.Path.GetFileNameWithoutExtension(path); }
            catch { return path; }
        }

        public void Dispose()
        {
            _pauseTimer?.Stop();
        }
    }
}