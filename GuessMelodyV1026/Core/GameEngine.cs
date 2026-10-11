using GuessMelody.Core.Enums;
using GuessMelody.Core.Models;
using GuessMelody.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GuessMelody.Core
{
    public class PressEvent
    {
        public string Mac { get; set; }
        public string TeamName { get; set; }
        public string Seq { get; set; }
        public long UptimeMs { get; set; }
        public DateTime ReceivedUtc { get; set; }
    }

    public class GameEngine : IDisposable
    {
        private readonly LogService _log;
        private readonly RoundTimer _countdownTimer;
        private readonly RoundTimer _answerTimer;
        private readonly Random _rng = new Random();
        

        private readonly List<PressEvent> _pressQueue = new List<PressEvent>();
        private readonly HashSet<string> _blockedThisRound =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Сколько треков каждой категории уже сыграно в текущей игре.
        // Ключ — имя категории (для устойчивости к пересканированию).
        private readonly Dictionary<string, HashSet<string>> _playedByCategory =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        private FeedbackService _feedback;
        public void AttachFeedback(FeedbackService feedback) => _feedback = feedback;

        public event EventHandler SettingsChanged;

        public event EventHandler<string> CorrectAnswerRequested;    // передаём Track.RelativePath
        public event EventHandler<string> WrongAnswerRequested;      // передаём Team.Name


        public GameSettings Settings { get; private set; } = new GameSettings();
        public RoundState State { get; private set; } = RoundState.Idle;
        public int CurrentRound { get; private set; }
        public int TotalRounds => Settings?.Rules?.RoundCount ?? 0;
        public Category CurrentCategory { get; private set; }
        public int CurrentCategoryIndex { get; private set; } = -1;
        public Track CurrentTrack { get; private set; }
        public Team FirstPressedTeam { get; private set; }
        public IReadOnlyList<PressEvent> PressQueue => _pressQueue;
        public IReadOnlyList<Team> Teams => Settings?.Buttons?.Teams ?? new List<Team>();

        public double CountdownLeftSec { get; private set; }
        public double AnswerLeftSec { get; private set; }
        public double TrackLeftSec { get; private set; }
        public double TrackTotalSec { get; private set; }

        // ---- События ----
        public event EventHandler StateChanged;
        public event EventHandler ScoreChanged;
        public event EventHandler<Track> TrackChanged;
        public event EventHandler<Team> FirstPressed;

        /// <summary>Категория активирована в новом раунде — можно мигать.</summary>
        public event EventHandler<Category> CategoryHighlighted;

        /// <summary>Изменился счётчик сыгранных/оставшихся треков в категориях.</summary>
        public event EventHandler RemainingChanged;

        public event EventHandler<Track> RequestPlayTrack;
        public event EventHandler RequestStopTrack;
        public event EventHandler RequestPauseTrack;
        public event EventHandler RequestResumeTrack;

        public GameEngine(LogService log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _countdownTimer = new RoundTimer();
            _answerTimer = new RoundTimer();

            _countdownTimer.Tick += (s, left) =>
            {
                CountdownLeftSec = left;
                StateChanged?.Invoke(this, EventArgs.Empty);
            };
            _countdownTimer.Timeout += (s, e) => OnCountdownFinished();

            _answerTimer.Tick += (s, left) =>
            {
                AnswerLeftSec = left;
                StateChanged?.Invoke(this, EventArgs.Empty);
            };
            _answerTimer.Timeout += (s, e) => OnAnswerTimeout();
        }

        public void ApplySettings(GameSettings settings)
        {
            Settings = settings ?? new GameSettings();
            ResetPlayedCounters();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            _log.Add(LogKind.Game, "Настройки применены");
        }


        /// <summary>Сообщить подписчикам, что настройки изменились (appearance и т.п.).</summary>
        public void NotifySettingsChanged() => SettingsChanged?.Invoke(this, EventArgs.Empty);


        /// <summary>Сбросить счётчики при новой игре.</summary>
        public void ResetPlayedCounters()
        {
            _playedByCategory.Clear();
            RemainingChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Сколько треков осталось в категории по индексу.</summary>
        public int GetRemaining(int categoryIndex)
        {
            var cats = Settings?.Folders?.Categories;
            if (cats == null || categoryIndex < 0 || categoryIndex >= cats.Count) return 0;

            var cat = cats[categoryIndex];
            int total = cat.Tracks?.Count ?? 0;
            if (!_playedByCategory.TryGetValue(cat.Name, out var played)) return total;
            return Math.Max(0, total - played.Count);
        }

        /// <summary>Сколько треков всего в категории по индексу.</summary>
        public int GetTotal(int categoryIndex)
        {
            var cats = Settings?.Folders?.Categories;
            if (cats == null || categoryIndex < 0 || categoryIndex >= cats.Count) return 0;
            return cats[categoryIndex].Tracks?.Count ?? 0;
        }

        // -------------------------------------------------------------
        // Раунд
        // -------------------------------------------------------------
        public void StartRound(int categoryIndex)
        {
            // --- Жёсткий стоп всей музыки от предыдущего раунда ---
            RequestStopTrack?.Invoke(this, EventArgs.Empty);
            _countdownTimer.Stop();
            _answerTimer.Stop();
            TrackLeftSec = 0;
            TrackTotalSec = 0;
            CountdownLeftSec = 0;
            AnswerLeftSec = 0;

            if (Settings?.Folders?.Categories == null || Settings.Folders.Categories.Count == 0)
            {
                _log.Add(LogKind.Error, "Нет категорий для старта раунда.");
                return;
            }

            if (categoryIndex < 0 || categoryIndex >= Settings.Folders.Categories.Count)
                categoryIndex = 0;

            var cat = Settings.Folders.Categories[categoryIndex];
            var track = PickTrack(cat);
            if (track == null)
            {
                _log.Add(LogKind.Game, $"Категория «{cat.Name}» пуста.");
                return;
            }

            CurrentCategory = cat;
            CurrentCategoryIndex = categoryIndex;
            CurrentTrack = track;
            FirstPressedTeam = null;
            _pressQueue.Clear();
            _blockedThisRound.Clear();

            _log.Add(LogKind.Game,
                $"Раунд {CurrentRound + 1} • Категория «{cat.Name}» • {track.RelativePath}");

            // Событие для мигания в UI — раньше, чем пойдёт музыка
            CategoryHighlighted?.Invoke(this, cat);
            TrackChanged?.Invoke(this, CurrentTrack);

            int cd = Settings.Timings.CountdownBeforeSec;
            if (cd > 0)
            {
                State = RoundState.Countdown;
                CountdownLeftSec = cd;
                RaiseState();
                _countdownTimer.Start(cd);
            }
            else
            {
                StartMusic();
            }
        }

        private void OnCountdownFinished() => StartMusic();

        private void StartMusic()
        {
            if (CurrentTrack == null) return;
            State = RoundState.Playing;

            TrackTotalSec = GetEffectiveDuration(CurrentTrack);
            TrackLeftSec = TrackTotalSec;

            RaiseState();
            RequestPlayTrack?.Invoke(this, CurrentTrack);
        }

        public void NextRound()
        {
            if (TotalRounds > 0 && CurrentRound + 1 >= TotalRounds)
            {
                RequestStopTrack?.Invoke(this, EventArgs.Empty);
                State = RoundState.Finished;
                RaiseState();
                _log.Add(LogKind.Game, "Игра завершена.");
                return;
            }
            RequestStopTrack?.Invoke(this, EventArgs.Empty);
            CurrentRound++;
            State = RoundState.Idle;
            RaiseState();
        }

        public void EndRound()
        {
            _answerTimer.Stop();
            _countdownTimer.Stop();
            RequestStopTrack?.Invoke(this, EventArgs.Empty);
            State = RoundState.Idle;
            RaiseState();
        }

        public void PauseMusic()
        {
            if (State == RoundState.Playing ||
                State == RoundState.WaitingForAnswer ||
                State == RoundState.Wrong)
                RequestPauseTrack?.Invoke(this, EventArgs.Empty);
        }

        public void ResumeMusic()
        {
            if (State == RoundState.WaitingForAnswer ||
                State == RoundState.Wrong ||
                State == RoundState.Playing)
            {
                RequestResumeTrack?.Invoke(this, EventArgs.Empty);

                // Если мы были в состоянии «ждём решения после неверного ответа» —
                // возвращаемся в игру и разблокируем команду, чтобы она могла нажать снова.
                if (State == RoundState.Wrong)
                {
                    //_blockedThisRound.Clear();   // снимаем блокировку — «Нет» можно отменить
                    State = RoundState.Playing;
                    RaiseState();
                }
            }
        }

        public void Panic()
        {
            _answerTimer.Stop();
            _countdownTimer.Stop();
            RequestStopTrack?.Invoke(this, EventArgs.Empty);
            State = RoundState.Idle;
            FirstPressedTeam = null;
            RaiseState();
            _log.Add(LogKind.Game, "PANIC: раунд сброшен.");
        }

        // -------------------------------------------------------------
        // Нажатие игрока
        // -------------------------------------------------------------
        public void OnPlayerPress(string mac, DateTime receivedUtc, string seq, long uptimeMs)
        {
            if (string.IsNullOrEmpty(mac)) return;

            if (Settings?.Rules?.IsSetupMode == true)
            {
                _log.Add(LogKind.Press, $"[SETUP] {GetTeamName(mac)} нажал (без учёта)");
                return;
            }

            if (State != RoundState.Playing && State != RoundState.WaitingForAnswer)
            {
                _log.Add(LogKind.Press, $"Игнор нажатия {GetTeamName(mac)} — состояние {State}");
                return;
            }

            // 1) Заблокирован — уже отвечал неверно в этом раунде
            if (_blockedThisRound.Contains(mac))
            {
                _log.Add(LogKind.Press, $"⛔ {GetTeamName(mac)} заблокирован — уже отвечал неверно.");
                return;
            }

            // 2) Уже нажимал этот MAC в этом раунде?
            var existing = _pressQueue.FirstOrDefault(p =>
                string.Equals(p.Mac, mac, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                // Перенажатие разрешено только если:
                //  - включена настройка AllowRePressAfterWrong
                //  - И мы всё ещё в Playing (никто не ждёт решения ведущего)
                bool allowRePress = Settings?.Rules?.AllowRePressAfterWrong == true
                                    && State == RoundState.Playing;

                if (!allowRePress)
                {
                    _log.Add(LogKind.Press, $"⛔ {GetTeamName(mac)} уже нажимал в этом раунде.");
                    return;
                }
            }

            var team = Teams.FirstOrDefault(t =>
                string.Equals(t.Mac, mac, StringComparison.OrdinalIgnoreCase));

            // 3) Первое нажатие в раунде — фиксируем
            bool isFirst = FirstPressedTeam == null && State == RoundState.Playing;

            if (isFirst)
            {
                var evt = new PressEvent
                {
                    Mac = mac,
                    TeamName = team?.Name ?? mac,
                    Seq = seq,
                    UptimeMs = uptimeMs,
                    ReceivedUtc = receivedUtc
                };
                _pressQueue.Add(evt);

                FirstPressedTeam = team;
                _log.Add(LogKind.Press, $"🎯 Первый: {evt.TeamName}");
                FirstPressed?.Invoke(this, team);

                RequestPauseTrack?.Invoke(this, EventArgs.Empty);
                State = RoundState.WaitingForAnswer;
                StartAnswerTimer();
                RaiseState();
                return;
            }

            // 4) Нажатие в состоянии WaitingForAnswer (пока ведущий решает) —
            //    просто добавляем в очередь для протокола, ничего не меняем.
            _pressQueue.Add(new PressEvent
            {
                Mac = mac,
                TeamName = team?.Name ?? mac,
                Seq = seq,
                UptimeMs = uptimeMs,
                ReceivedUtc = receivedUtc
            });
            _log.Add(LogKind.Press, $"📝 {GetTeamName(mac)} нажал (ожидание решения ведущего)");
        }

        private void StartAnswerTimer()
        {
            int timeout = Settings?.Timings?.AnswerTimeoutSec ?? 0;
            if (timeout > 0)
            {
                AnswerLeftSec = timeout;
                _answerTimer.Start(timeout);
            }
        }

        private void OnAnswerTimeout()
        {
            _log.Add(LogKind.Game, "⌛ Время на ответ истекло.");
            State = RoundState.Scored;
            RaiseState();
        }

        // -------------------------------------------------------------
        // Очки
        // -------------------------------------------------------------

        public void SubmitScore(bool correct)
        {
            if (FirstPressedTeam == null)
            {
                _log.Add(LogKind.Game, "Очки: некому начислять (нет нажатия).");
                return;
            }

            if (correct)
            {
                int add = Settings?.Rules?.ScorePerCorrect ?? 1;
                FirstPressedTeam.Score += add;
                _log.Add(LogKind.Score,
                    $"✅ {FirstPressedTeam.Name} +{add} → {FirstPressedTeam.Score}");

                RequestStopTrack?.Invoke(this, EventArgs.Empty);
                _answerTimer.Stop();
                State = RoundState.Scored;
                RaiseState();
                ScoreChanged?.Invoke(this, EventArgs.Empty);

                // Событие для показа правильного ответа
                CorrectAnswerRequested?.Invoke(this, CurrentTrack?.RelativePath ?? "");
                return;
            }

            // ---- «Нет» ----
            _log.Add(LogKind.Score, $"❌ {FirstPressedTeam.Name} — ответ неверный.");
            _blockedThisRound.Add(FirstPressedTeam.Mac);
            var wrongTeamName = FirstPressedTeam.Name;
            FirstPressedTeam = null;
            _answerTimer.Stop();

            // Событие для показа «Неверный ответ» + паузы
            WrongAnswerRequested?.Invoke(this, wrongTeamName);

            if (Settings?.Rules?.AllowRePressAfterWrong == true)
            {
                // Пауза сделана сервисом; музыка будет возобновлена по его сигналу
                // (см. FeedbackService.WrongPauseFinished → ResumeMusic)
                State = RoundState.Wrong;
                RaiseState();
            }
            else
            {
                RequestPauseTrack?.Invoke(this, EventArgs.Empty);
                State = RoundState.Wrong;
                RaiseState();
            }
        }

        // -------------------------------------------------------------
        // Управление командами на лету (эмуляция, отладка, привязка)
        // -------------------------------------------------------------

        /// <summary>Создать/обновить команду по номеру 1..6.</summary>
        public Team EnsureTeam(int id, string name, string mac = null)
        {
            if (Settings.Buttons == null) Settings.Buttons = new ButtonsMap();
            var list = Settings.Buttons.Teams;

            var team = list.FirstOrDefault(t => t.Id == id);
            if (team == null)
            {
                team = new Team
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(name) ? $"Игрок {id}" : name,
                    Mac = mac ?? $"VIRT:{id:00}",
                    Score = 0
                };
                list.Add(team);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(name)) team.Name = name;
                if (!string.IsNullOrWhiteSpace(mac)) team.Mac = mac;
            }

            _log.Add(LogKind.Game, $"Команда #{id}: {team.Name} ({team.Mac})");
            ScoreChanged?.Invoke(this, EventArgs.Empty);
            return team;
        }

        /// <summary>Сбросить очки всем командам.</summary>
        public void ResetScores()
        {
            foreach (var t in Teams) t.Score = 0;
            ScoreChanged?.Invoke(this, EventArgs.Empty);
            _log.Add(LogKind.Game, "Очки всех команд сброшены.");
        }

        /// <summary>Найти команду по номеру.</summary>
        public Team GetTeamById(int id) => Teams.FirstOrDefault(t => t.Id == id);

        /// <summary>
        /// Эмуляция нажатия виртуальной кнопки игрока с номером 1..6.
        /// Если команды нет — создаст с MAC VIRT:NN.
        /// </summary>
        public void EmulatePress(int teamId)
        {
            var team = GetTeamById(teamId) ?? EnsureTeam(teamId, $"Игрок {teamId}");
            OnPlayerPress(team.Mac, DateTime.UtcNow, "virt", 0);
        }
        /// <summary>
        /// Эмуляция heartbeat от игрока с номером 1..6.
        /// Если команды ещё нет — создаст с MAC VIRT:NN.
        /// </summary>
        public void EmulateHeartbeat(int teamId)
        {
            var team = GetTeamById(teamId) ?? EnsureTeam(teamId, $"Игрок {teamId}");
            OnHeartbeat(team.Mac);
        }

        /// <summary>Зарегистрировать heartbeat команды по MAC. Обновляет LastHeartbeatUtc и IsAlive.</summary>
        public void OnHeartbeat(string mac)
        {
            var team = Teams.FirstOrDefault(t =>
                string.Equals(t.Mac, mac, StringComparison.OrdinalIgnoreCase));
            if (team == null) return;

            team.LastHeartbeatUtc = DateTime.UtcNow;
            team.IsAlive = true;
            team.HeartbeatCount++;

            Heartbeat?.Invoke(this, team);
        }

        /// <summary>Периодический тик: помечает команды без HB > timeout как неживые.</summary>
        public void TickHeartbeats(double timeoutSec)
        {
            var now = DateTime.UtcNow;
            bool changed = false;

            foreach (var t in Teams)
            {
                bool alive = t.LastHeartbeatUtc != DateTime.MinValue &&
                             (now - t.LastHeartbeatUtc).TotalSeconds <= timeoutSec;

                if (t.IsAlive != alive)
                {
                    t.IsAlive = alive;
                    changed = true;
                }
            }

            if (changed) HeartbeatChanged?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler<Team> Heartbeat;
        public event EventHandler HeartbeatChanged;

        public void AdjustScore(string mac, int delta)
        {
            var team = Teams.FirstOrDefault(t =>
                string.Equals(t.Mac, mac, StringComparison.OrdinalIgnoreCase));
            if (team == null) return;

            team.Score += delta;
            if (team.Score < 0) team.Score = 0;
            _log.Add(LogKind.Score, $"{team.Name} {delta:+#;-#;0} → {team.Score}");
            ScoreChanged?.Invoke(this, EventArgs.Empty);
            RaiseState();
        }

        /// <summary>Полный сброс игрового состояния: очки, раунды, использованные треки, очередь нажатий.</summary>
        public void NewGame()
        {
            RequestStopTrack?.Invoke(this, EventArgs.Empty);
            _countdownTimer.Stop();
            _answerTimer.Stop();

            foreach (var t in Teams)
            {
                t.Score = 0;
                t.LastHeartbeatUtc = DateTime.MinValue;
                t.IsAlive = false;
                t.HeartbeatCount = 0;
            }

            _playedByCategory.Clear();
            _pressQueue.Clear();
            _blockedThisRound.Clear();

            CurrentRound = 0;
            CurrentCategory = null;
            CurrentCategoryIndex = -1;
            CurrentTrack = null;
            FirstPressedTeam = null;
            State = RoundState.Idle;
            CountdownLeftSec = 0;
            AnswerLeftSec = 0;
            TrackLeftSec = 0;
            TrackTotalSec = 0;

            ScoreChanged?.Invoke(this, EventArgs.Empty);
            RemainingChanged?.Invoke(this, EventArgs.Empty);
            StateChanged?.Invoke(this, EventArgs.Empty);
            
            _log.Add(LogKind.Game, "Новая игра — состояние сброшено.");
        }

        // -------------------------------------------------------------
        // Выбор трека
        // -------------------------------------------------------------
        private Track PickTrack(Category cat)
        {
            if (cat?.Tracks == null || cat.Tracks.Count == 0) return null;

            if (!_playedByCategory.TryGetValue(cat.Name, out var played))
            {
                played = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _playedByCategory[cat.Name] = played;
            }

            var pool = cat.Tracks
                .Where(t => !played.Contains(t.RelativePath))
                .ToList();

            if (pool.Count == 0)
            {
                // Все сыграны — сбрасываем пул этой категории
                played.Clear();
                pool = cat.Tracks.ToList();
            }

            var pick = pool[_rng.Next(pool.Count)];
            played.Add(pick.RelativePath);

            // Сообщаем UI, что счётчики изменились
            RemainingChanged?.Invoke(this, EventArgs.Empty);

            return pick;
        }

        private string GetTeamName(string mac)
        {
            var t = Teams.FirstOrDefault(x =>
                string.Equals(x.Mac, mac, StringComparison.OrdinalIgnoreCase));
            return t?.Name ?? mac;
        }

        private double GetEffectiveDuration(Track t)
        {
            var ov = t.Overrides;
            if (ov?.DurationSec != null) return ov.DurationSec.Value;
            if (Settings?.Rules != null && Settings.Rules.FragmentSec > 0) return Settings.Rules.FragmentSec;
            return 20;
        }

        public void TickPlayback(double trackLeftSec)
        {
            TrackLeftSec = trackLeftSec;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RaiseState() => StateChanged?.Invoke(this, EventArgs.Empty);

        public void Dispose()
        {
            _countdownTimer?.Dispose();
            _answerTimer?.Dispose();
        }
    }
}