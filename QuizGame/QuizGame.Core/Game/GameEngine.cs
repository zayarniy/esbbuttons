using QuizGame.Audio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace QuizGame.QuizGame.Core.Game
{
    public enum GamePhase
    {
        Idle,              // ничего не происходит
        CategorySelect,    // ждём выбор категории
        Countdown,         // 3..2..1 перед треком
        Playing,           // играет отрывок
        AwaitingAnswer,    // музыка на паузе, кто-то нажал, ждём оценку
        RoundFinished      // раунд сыгран (ждём следующего действия ведущего)
    }

    public class GameEngine : IDisposable
    {
        private readonly GameSettings _settings;
        private readonly AudioPlayer _music;
        private readonly SoundBank _sfx;
        private readonly DispatcherTimer _tickTimer;      // 100 мс
        private readonly DispatcherTimer _countdownTimer; // 1 с
        private readonly Random _rnd = new Random();

        public GamePhase Phase { get; private set; } = GamePhase.Idle;
        public double PhaseElapsedSec { get; private set; }
        public double PhaseDurationSec { get; private set; }

        public TrackInfo CurrentTrack { get; private set; }
        public CategoryInfo CurrentCategory { get; private set; }
        public DateTime? PressAtUtc { get; private set; }
        public PlayerButton FirstPressedPlayer { get; private set; }

        // События для UI
        public event EventHandler<GamePhase> PhaseChanged;
        public event EventHandler<(string, int)> CountdownTick;      // (текст, секунды)
        public event EventHandler<TrackInfo> TrackStarted;
        public event EventHandler TrackStopped;
        public event EventHandler<PlayerButton> FirstPressed;
        public event EventHandler<(PlayerButton, int)> ScoreAwarded;
        public event EventHandler RoundEnded;

        public GameEngine(GameSettings settings, AudioPlayer music, SoundBank sfx)
        {
            _settings = settings; _music = music; _sfx = sfx;

            _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _tickTimer.Tick += (_, __) => OnTick();

            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += (_, __) => OnCountdownTick();
        }

        // ==== API ====

        public void SelectCategory(CategoryInfo cat)
        {
            if (Phase != GamePhase.Idle && Phase != GamePhase.RoundFinished) return;
            if (cat == null || cat.Tracks.Count == 0) return;

            CurrentCategory = cat;
            SetPhase(GamePhase.Countdown, _settings.StartCountdownSec);

            if (_settings.PlayRoundStartSound)
                _sfx.Play("round_start");

            _countdownTimer.Start();
        }

        public void OnButtonPressed(string mac, PlayerButton player)
        {
            // Игрок может нажать только в фазе Playing
            if (Phase != GamePhase.Playing) return;

            FirstPressedPlayer = player;
            PressAtUtc = DateTime.UtcNow;

            if (_settings.PauseMusicOnPress)
                _music.Pause();

            FirstPressed?.Invoke(this, player);

            // Переходим в ожидание оценки
            SetPhase(GamePhase.AwaitingAnswer, _settings.AnswerTimeSec);
        }

        public void AwardScore(int score)
        {
            if (Phase != GamePhase.AwaitingAnswer) return;

            // Звук для этой кнопки
            var sb = _settings.ScoreButtons.FirstOrDefault(s => s.Score == score);
            if (sb != null && !string.IsNullOrEmpty(sb.SoundPath))
                _sfx.Play($"score_{score}");

            ScoreAwarded?.Invoke(this, (FirstPressedPlayer, score));
            EndRound();
        }

        public void SkipRound() => EndRound();

        // ==== Внутренняя кухня ====

        private void SetPhase(GamePhase p, double duration = 0)
        {
            Phase = p;
            PhaseElapsedSec = 0;
            PhaseDurationSec = duration;
            _tickTimer.Start();
            PhaseChanged?.Invoke(this, p);

            if (p == GamePhase.Playing) StartRandomTrack();
        }

        private void OnTick()
        {
            PhaseElapsedSec += 0.1;

            // Авто-финиш фаз по времени
            if (Phase == GamePhase.Playing && PhaseElapsedSec >= _settings.PlayDurationSec)
            {
                _music.Pause();
                SetPhase(GamePhase.Idle); // ждём нажатия или ручного решения
            }
            else if (Phase == GamePhase.AwaitingAnswer && PhaseElapsedSec >= _settings.AnswerTimeSec)
            {
                // Никто не ответил — снимаем
                EndRound();
            }
        }

        private void OnCountdownTick()
        {
            var left = (int)Math.Ceiling(_settings.StartCountdownSec - PhaseElapsedSec);
            CountdownTick?.Invoke(this, (left.ToString(), left));

            if (PhaseElapsedSec >= _settings.StartCountdownSec)
            {
                _countdownTimer.Stop();
                SetPhase(GamePhase.Playing, _settings.PlayDurationSec);
            }
        }

        private void StartRandomTrack()
        {
            var tracks = CurrentCategory.Tracks;
            if (tracks.Count == 0) { EndRound(); return; }

            var t = tracks[_rnd.Next(tracks.Count)];
            CurrentTrack = t;

            double startSec = _settings.UseRandomStart && t.DurationSec > _settings.PlayDurationSec
                ? _rnd.NextDouble() * (t.DurationSec - _settings.PlayDurationSec)
                : _settings.StartAtSec;

            _music.Load(t.FullPath, startSec);
            _music.Play();
            TrackStarted?.Invoke(this, t);
        }

        private void EndRound()
        {
            _music.Stop();
            _countdownTimer.Stop();
            SetPhase(GamePhase.RoundFinished);
            RoundEnded?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            _tickTimer.Stop();
            _countdownTimer.Stop();
            _music?.Dispose();
        }
    }
}
