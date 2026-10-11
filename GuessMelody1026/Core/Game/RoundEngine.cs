using GuessMelody.Core.Audio;
using GuessMelody.Core.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Timers;

namespace GuessMelody.Core.Game
{
    /// <summary>
    /// Автомат раунда: категория → отсчёт → трек → нажатие → ответ → балл.
    /// </summary>
    public sealed class RoundEngine : IDisposable
    {
        // --- Внешние зависимости ---
        private readonly IAudioEngine _audio;
        private readonly GameSettings _settings;
        private readonly FolderManager _folders;

        // --- Таймеры ---
        private readonly Timer _tickTimer = new Timer(100);    // для UI-обновлений
        private DateTime _playDeadline;
        private DateTime _answerDeadline;
        private int _countdownRemaining;

        private readonly Timer _countdownTimer = new Timer(1000); // для отсчёта 3-2-1


        // --- Состояние ---
        public RoundState State { get; private set; } = RoundState.Idle;
        public int RoundNumber { get; private set; }
        public CategoryConfig Category { get; private set; }
        public string TrackFile { get; private set; }
        public PressEvent CurrentWinner { get; private set; }
        public int CurrentScore { get; private set; }
        public TimeSpan TimeLeftForAnswer => _answerDeadline - DateTime.UtcNow;

        public IReadOnlyList<RoundRecord> History => _history;

        // --- Внутренние ---
        private readonly PressArbiter _arbiter = new PressArbiter();
        private readonly HashSet<string> _playedTracks =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<RoundRecord> _history = new List<RoundRecord>();
        private Random _rng = new Random();

        // --- Для RoundRecord ---
        private DateTime _roundStartedUtc;
        private DateTime? _pauseMoment;
        private double _answerSecondsElapsed;

        // --- Внешние MAC-и (для проверки «все ответили неверно») ---
        private List<string> _knownMacs;

        // --- События ---
        public event EventHandler<RoundState> StateChanged;
        public event EventHandler<string> Message;
        public event EventHandler<PressEvent> PressAccepted;
        public event EventHandler<RoundRecord> RoundFinished;
        public event EventHandler<int> CountdownTick;

        public RoundEngine(IAudioEngine audio, GameSettings settings, FolderManager folders)
        {
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _folders = folders ?? throw new ArgumentNullException(nameof(folders));

            _tickTimer.AutoReset = true;
            _tickTimer.Elapsed += OnTick;

            _countdownTimer.AutoReset = true;
            _countdownTimer.Elapsed += OnCountdownTick;
        }

        private void OnCountdownTick(object sender, ElapsedEventArgs e)
        {
            if (State != RoundState.Countdown)
            {
                _countdownTimer.Stop();
                return;
            }

            _countdownRemaining--;

            if (_countdownRemaining > 0)
            {
                // Тик и ждём окончания, чтобы наложений не было
                //_audio.PlayOneShotAndWait(_settings.CountdownTickSoundFile, maxWaitMs: 1500);
                CountdownTick?.Invoke(this, _countdownRemaining);
            }
            else
            {
                _countdownTimer.Stop();
                // Прощальный «go», ждём, потом музыка
                _audio.PlayOneShotAndWait(_settings.CountdownEndSoundFile, maxWaitMs: 2000);
                BeginTrack();
            }
        }

        // =============================================================
        // УПРАВЛЕНИЕ РАУНДОМ
        // =============================================================

        /*public void StartRound(CategoryConfig category)
        {
            if (category == null) { EndRound(EndReason.Manual); return; }

            RoundNumber++;
            Category = category;
            CurrentWinner = null;
            CurrentScore = 0;
            _answerSecondsElapsed = 0;
            _pauseMoment = null;
            _roundStartedUtc = DateTime.UtcNow;

            _arbiter.Reset();

            var track = PickNextTrack(category);
            if (track == null)
            {
                Message?.Invoke(this, $"В категории «{category.Name}» не осталось треков.");
                EndRound(EndReason.NoTracks);
                return;
            }

            TrackFile = track;
            Message?.Invoke(this, $"Раунд {RoundNumber}: {category.Name} → {track}");

            if (_settings.PlayRoundStartSound)
                TryPlay(_settings.RoundStartSoundFile);

            if (_settings.PlayCountdown && _settings.CountdownSeconds > 0)
            {
                _countdownRemaining = _settings.CountdownSeconds;
                SetState(RoundState.Countdown);
                CountdownTick?.Invoke(this, _countdownRemaining);
                _tickTimer.Start();
            }
            else
            {
                BeginTrack();
            }
        }*/

        public void StartRound(CategoryConfig category)
        {
            if (category == null) { EndRound(EndReason.Manual); return; }

            RoundNumber++;
            Category = category;
            CurrentWinner = null;
            CurrentScore = 0;
            _answerSecondsElapsed = 0;
            _pauseMoment = null;
            _roundStartedUtc = DateTime.UtcNow;
            _arbiter.Reset();

            var track = PickNextTrack(category);
            if (track == null)
            {
                Message?.Invoke(this, $"В категории «{category.Name}» не осталось треков.");
                EndRound(EndReason.NoTracks);
                return;
            }

            TrackFile = track;
            Message?.Invoke(this, $"Раунд {RoundNumber}: {category.Name} → {track}");

            SetState(RoundState.RoundStart);

            // Звук старта раунда — дождаться его окончания
            if (_settings.PlayRoundStartSound)
            {
                _audio.PlayOneShotAndWait(_settings.RoundStartSoundFile, maxWaitMs: 3000);
            }

            // Обратный отсчёт или сразу трек
            if (_settings.PlayCountdown && _settings.CountdownSeconds > 0)
            {
                _countdownRemaining = _settings.CountdownSeconds;
                SetState(RoundState.Countdown);

                // Первый тик играем сразу + ждём окончания
                //_audio.PlayOneShotAndWait(_settings.CountdownTickSoundFile, maxWaitMs: 1500);
                CountdownTick?.Invoke(this, _countdownRemaining);

                // Дальше — по секундам через _countdownTimer
                _countdownTimer.Start();
            }
            else
            {
                BeginTrack();
            }
        }
        //public void StartRound(CategoryConfig category)
        //{
        //    if (category == null) { EndRound(EndReason.Manual); return; }

        //    RoundNumber++;
        //    Category = category;
        //    CurrentWinner = null;
        //    CurrentScore = 0;
        //    _answerSecondsElapsed = 0;
        //    _pauseMoment = null;
        //    _roundStartedUtc = DateTime.UtcNow;

        //    _arbiter.Reset();

        //    var track = PickNextTrack(category);
        //    if (track == null)
        //    {
        //        Message?.Invoke(this, $"В категории «{category.Name}» не осталось треков.");
        //        EndRound(EndReason.NoTracks);
        //        return;
        //    }

        //    TrackFile = track;
        //    Message?.Invoke(this, $"Раунд {RoundNumber}: {category.Name} → {track}");

        //    SetState(RoundState.RoundStart);

        //    // Звук старта раунда + пауза до его окончания
        //    if (_settings.PlayRoundStartSound)
        //    {
        //        var waited = _audio.PlayOneShotAndWait(_settings.RoundStartSoundFile,
        //                                               maxWaitMs: 3000);
        //        Message?.Invoke(this, $"Звук старта раунда: {waited.TotalMilliseconds:F0} мс");
        //    }

        //    // Затем — обратный отсчёт или сразу трек
        //    if (_settings.PlayCountdown && _settings.CountdownSeconds > 0)
        //    {
        //        _countdownRemaining = _settings.CountdownSeconds;
        //        SetState(RoundState.Countdown);
        //        CountdownTick?.Invoke(this, _countdownRemaining);
        //        // Первый тик играем сразу же
        //        _audio.PlayOneShotAndWait(_settings.CountdownTickSoundFile, maxWaitMs: 1500);
        //        _tickTimer.Start();
        //    }
        //    else
        //    {
        //        BeginTrack();
        //    }
        //}

        /// <summary>Ведущий нажал «Да» — применить балл и завершить раунд.</summary>
        public void HostSaysYes(int score=1)
        {
            if (State != RoundState.WaitingAnswer && State != RoundState.TrackPlaying)
                return;

            CurrentScore = score;
            TryPlay(_settings.RightSoundFile);
            Message?.Invoke(this, $"+{score} для {CurrentWinner?.Mac ?? "?"}");

            MarkTrackAsPlayed();
            SetState(RoundState.ScoreApplied);
            EndRound(EndReason.Scored);
        }

        /// <summary>Ведущий нажал «Нет» — вернуть музыку, продолжить ждать других.</summary>
        public void HostSaysNo()
        {
            if (State != RoundState.WaitingAnswer) return;
            if (CurrentWinner == null) return;

            _audio.PlayOneShot(_settings.WrongSoundFile);

            // Заблокировать этого игрока до конца раунда
            _arbiter.Reject(CurrentWinner.Mac);
            Message?.Invoke(this, $"Неверно: {CurrentWinner.Mac} выбывает из раунда");
            CurrentWinner = null;

            // Все ответили неверно — закрываем раунд
            if (_knownMacs != null && _arbiter.EveryoneRejected(_knownMacs))
            {
                Message?.Invoke(this, "Все ответили неверно — раунд закрыт.");
                MarkTrackAsPlayed();
                EndRound(EndReason.AllAnsweredWrong);
                return;
            }

            // Возвращаемся к музыке и продолжаем ждать других
            _audio.Play();                     // возобновление с той же позиции
            _playDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(_settings.PlayDurationSec);
            SetState(RoundState.TrackPlaying);
        }

        /// <summary>Ведущий принудительно закрывает раунд (никто / вручную).</summary>
        public void HostSaysNoOne()
        {
            if (State == RoundState.Idle) return;
            MarkTrackAsPlayed();
            EndRound(EndReason.Manual);
        }

        // =============================================================
        // СОБЫТИЯ ОТ ESP
        // =============================================================

        public void OnPress(PressEvent e)
        {
            if (State != RoundState.TrackPlaying) return;

            // Уже отвечал в этом раунде — игнор.
            if (!_arbiter.OnPress(e)) return;

            var first = _arbiter.GetFirst();
            if (first == null) return;
            if (CurrentWinner != null) return;   // кто-то уже отвечает

            CurrentWinner = first;
            _audio.Pause();
            _pauseMoment = DateTime.UtcNow;
            _answerSecondsElapsed = 0;
            _answerDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(_settings.AnswerSeconds);

            SetState(RoundState.WaitingAnswer);
            PressAccepted?.Invoke(this, first);
            Message?.Invoke(this, $"Нажал первым: {first.Mac}");
        }
        // =============================================================
        // ВНУТРЕННЕЕ
        // =============================================================
        public void SetKnownMacs(IEnumerable<string> macs)
        {
            _knownMacs = macs?.Where(m => !string.IsNullOrEmpty(m)).ToList();
        }

        private void OnTick(object sender, ElapsedEventArgs e)
        {
            if (State == RoundState.TrackPlaying)
            {
                if (DateTime.UtcNow >= _playDeadline)
                {
                    Message?.Invoke(this, "Время проигрывания истекло — никто не нажал.");
                    MarkTrackAsPlayed();
                    EndRound(EndReason.NoOne);
                }
                return;
            }

            if (State == RoundState.WaitingAnswer)
            {
                if (_pauseMoment.HasValue)
                    _answerSecondsElapsed = (DateTime.UtcNow - _pauseMoment.Value).TotalSeconds;

                if (DateTime.UtcNow >= _answerDeadline)
                {
                    Message?.Invoke(this, "Время ответа истекло — ответ не засчитан.");

                    // То же, что «Нет»: блокируем игрока, музыка продолжается.
                    if (CurrentWinner != null)
                        _arbiter.Reject(CurrentWinner.Mac);

                    CurrentWinner = null;
                    _audio.Play();
                    _playDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(_settings.PlayDurationSec);
                    SetState(RoundState.TrackPlaying);
                }
                return;
            }
        }

        private void BeginTrack()
        {
            // 1) Полный путь к mp3
            string fullPath = _folders.GetFullTrackPath(Category, TrackFile);

            if (string.IsNullOrEmpty(fullPath) || !System.IO.File.Exists(fullPath))
            {
                Message?.Invoke(this, $"Файл трека не найден: {fullPath}");
                SetState(RoundState.Idle);
                return;
            }

            try
            {
                _audio.Load(fullPath);

                TimeSpan startPos = PickStartPosition();
                if (startPos > TimeSpan.Zero) _audio.Seek(startPos);

                _audio.Play();

                Message?.Invoke(this, $"▶ Играет: {System.IO.Path.GetFileName(fullPath)}");
            }
            catch (Exception ex)
            {
                Message?.Invoke(this, $"Ошибка воспроизведения {fullPath}: {ex.Message}");
                SetState(RoundState.Idle);
                return;
            }

            _playDeadline = DateTime.UtcNow +
                TimeSpan.FromSeconds(_settings.PlayDurationSec);
            SetState(RoundState.TrackPlaying);
            _tickTimer.Interval = 1000;
            _tickTimer.Start();
        }

        private TimeSpan PickStartPosition()
        {
            switch (_settings.StartAtMode)
            {
                case StartAtMode.FromBeginning:
                    return TimeSpan.Zero;

                case StartAtMode.FromSpecificTime:
                    return TimeSpan.FromSeconds(_settings.StartAtSec);

                case StartAtMode.FromRandomPlace:
                default:
                    var dur = _audio.Duration > TimeSpan.Zero
                        ? _audio.Duration
                        : TimeSpan.FromSeconds(60);
                    var maxStart = dur.TotalSeconds * 0.6;
                    if (maxStart < 1) return TimeSpan.Zero;
                    return TimeSpan.FromSeconds(_rng.NextDouble() * maxStart);
            }
        }

        private string PickNextTrack(CategoryConfig cat)
        {
            var pool = cat.Tracks
                .Where(t => !_playedTracks.Contains(Key(cat, t)))
                .ToList();
            if (pool.Count == 0) return null;
            return pool[_rng.Next(pool.Count)];
        }

        private void MarkTrackAsPlayed()
        {
            if (Category != null && !string.IsNullOrEmpty(TrackFile))
                _playedTracks.Add(Key(Category, TrackFile));
        }

        private static string Key(CategoryConfig cat, string track) =>
            $"{cat.RelativePath}|{track}";

        public int TracksLeft(CategoryConfig cat)
        {
            if (cat == null) return 0;
            return cat.Tracks.Count(t => !_playedTracks.Contains(Key(cat, t)));
        }

        public void ResetPlayedTracks() => _playedTracks.Clear();

        private void TryPlay(string file)
        {
            if (string.IsNullOrWhiteSpace(file)) return;
            try { _audio.PlayOneShot(file); }
            catch (Exception ex) { Message?.Invoke(this, $"OneShot {file}: {ex.Message}"); }
        }

        private void SetState(RoundState s)
        {
            if (State == s) return;
            State = s;
            StateChanged?.Invoke(this, s);
        }

        private void EndRound(EndReason reason)
        {
            _tickTimer.Stop();
            _countdownTimer.Stop();
            // Если пауза была — досчитаем реальное время ответа
            if (_pauseMoment.HasValue)
                _answerSecondsElapsed =
                    (DateTime.UtcNow - _pauseMoment.Value).TotalSeconds;

            var record = new RoundRecord
            {
                RoundNumber = RoundNumber,
                Category = Category?.Name,
                TrackFile = TrackFile,
                WinnerMac = CurrentWinner?.Mac,
                WinnerName = null,               // заполнит GameController
                Score = CurrentScore,
                Reason = reason,
                StartedUtc = _roundStartedUtc,
                FinishedUtc = DateTime.UtcNow,
                AnswerTimeSec = _answerSecondsElapsed
            };

            _history.Add(record);
            RoundFinished?.Invoke(this, record);

            CurrentWinner = null;
            TrackFile = null;
            Category = null;
            _pauseMoment = null;
            _answerSecondsElapsed = 0;
            CurrentScore = 0;
            SetState(RoundState.Idle);
        }

        public void Dispose() {
            _tickTimer.Stop();
            _tickTimer.Elapsed -= OnTick;   // ← отписка
            _tickTimer.Dispose();
            _countdownTimer.Stop();
            _countdownTimer.Elapsed -= OnCountdownTick;   // ← отписка
            _countdownTimer.Dispose();

        }

        
    }
}