using GuessMelody.Core.Audio;
using GuessMelody.Core.Game;
using GuessMelody.Core.Models;
using GuessMelody.Wpf.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GuessMelody.Wpf.Game
{
    /// <summary>
    /// Центральный оркестратор игры.
    /// Один экземпляр на приложение, все окна работают через него.
    /// </summary>
    public sealed class GameController
    {
        private static readonly Lazy<GameController> _instance =
            new Lazy<GameController>(() => new GameController());
        public static GameController Instance => _instance.Value;

        // =============================================================
        // Зависимости (задаются в Initialize)
        // =============================================================
        private IAudioEngine _audio;
        private FolderManager _folders;
        private ButtonService _buttons;
        private GameSettings _settings;
        private RoundEngine _engine;

        public RoundEngine Engine => _engine;
        public ButtonService Buttons => _buttons;
        public GameSettings Settings => _settings;
        public FolderManager Folders => _folders;

        // =============================================================
        // Состояние игры
        // =============================================================
        private readonly Dictionary<string, int> _scores =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private readonly List<RoundRecord> _history = new List<RoundRecord>();
        public IReadOnlyList<RoundRecord> History => _history;

        public event EventHandler StateChanged;
        public event EventHandler<PressEvent> PressReceived;
        public event EventHandler<string> ScoreChanged;   // mac
        public event EventHandler<RoundRecord> RoundFinished;
        public event EventHandler<string> Message;

        private GameController() { }

        // =============================================================
        // Инициализация
        // =============================================================
        public void Initialize(IAudioEngine audio, FolderManager folders,
                               ButtonService buttons, GameSettings settings)
        {
            _audio = audio;
            _folders = folders;
            _buttons = buttons;
            _settings = settings;


            ResetEngine();
            _engine = new RoundEngine(audio, settings, folders);
            _engine.StateChanged += OnEngineStateChanged;
            _engine.Message += OnEngineMessage;
            _engine.PressAccepted += OnEnginePressAccepted;
            _engine.RoundFinished += OnEngineRoundFinished;
            AttachButtons();

        }
            // Прокидываем события наружу
            private void OnEngineStateChanged(object s, RoundState st)
        {
            AppLogger.Instance.State($"Состояние: {st}");
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        private void OnEngineMessage(object s, string m) { AppLogger.Instance.Info(m); }
        private void OnEnginePressAccepted(object s, PressEvent p)
        {
            AppLogger.Instance.Press($"Принят ответ: {p.Mac}");
            PressReceived?.Invoke(this, p);
        }
        private void OnEngineRoundFinished(object s, RoundRecord r) {
            AppLogger.Instance.State(
                $"Раунд {r.RoundNumber} завершён: {r.Reason}, " +
                $"категория «{r.Category}», победитель «{r.WinnerName ?? "—"}», " +
                $"балл {r.Score}");
               OnRoundFinished(r);

        }
        //_engine.StateChanged += (s, st) =>
        //    {
        //        AppLogger.Instance.State($"Состояние: {st}");
        //        StateChanged?.Invoke(this, EventArgs.Empty);
        //    };

        //_engine.Message += (s, m) => AppLogger.Instance.Info(m);
        //_engine.PressAccepted += (s, p) =>
        //    {
        //        AppLogger.Instance.Press($"Принят ответ: {p.Mac}");
        //        PressReceived?.Invoke(this, p);
        //    };
        //    _engine.RoundFinished += (s, r) =>
        //    {
        //    };

            // Подписка на нажатия — если ButtonService появится позже,
            // вызывающая сторона должна дёрнуть AttachButtons().

        /// <summary>Переподключиться к ButtonService (после Start/Stop сервера).</summary>
        public void AttachButtons()
        {
            if (_buttons == null) return;

            _buttons.PressReceived -= OnPress;
            _buttons.PressReceived += OnPress;
        }

        public void ResetEngine()
        {
            if (_engine != null)
            {
                // отписываемся
                _engine.StateChanged -= OnEngineStateChanged;
                _engine.Message -= OnEngineMessage;
                _engine.PressAccepted -= OnEnginePressAccepted;
                _engine.RoundFinished -= OnEngineRoundFinished;

                // останавливаем таймер
                try { _engine.Dispose(); } catch { }
                _engine = null;
            }
        }


        /// <summary>Изменить счёт игрока на delta (+1/-1/...).</summary>
        public void AddScore(string mac, int delta)
        {
            if (string.IsNullOrEmpty(mac)) return;

            _scores.TryGetValue(mac, out var current);
            int next = current + delta;

            // Можно ограничить снизу нулём, если нужно:
            // if (next < 0) next = 0;

            _scores[mac] = next;
            AppLogger.Instance.Score(
                $"{GetDisplayName(mac)}: {current} → {next} ({(delta >= 0 ? "+" : "")}{delta})");
            ScoreChanged?.Invoke(this, mac);
        }

        private string GetDisplayName(string mac)
        {
            var snap = _buttons?.GetByMac(mac);
            return snap?.Label ?? mac;
        }
        // запомните делегаты-обёртки, чтобы можно было отписаться:
        //private void OnEngineStateChanged(object s, RoundState st) { ... }
        //private void OnEngineMessage(object s, string m) { ... }
        //private void OnEnginePressAccepted(object s, PressEvent p) { ... }
        //private void OnEngineRoundFinished(object s, RoundRecord r) { ... }

        // =============================================================
        // Публичные команды
        // =============================================================
        public void StartRound(int categoryIndex)
        {
            if (_folders == null) return;
            var cat = _folders.GetCategory(categoryIndex);
            if (cat == null) return;

            // Обновить список известных MAC — нужно RoundEngine для «все ответили неверно».
            UpdateKnownMacs();

            _engine.StartRound(cat);
        }

        public void HostSaysYes(int score=1)
        {
            var winner = _engine.CurrentWinner;
            if (winner == null) return;

            // Обновляем счёт до завершения раунда — чтобы UI увидел новое значение
            var mac = winner.Mac;
            _scores.TryGetValue(mac, out var current);
            _scores[mac] = current + score;

            // Сохраним в запись раунда
            var lastStarted = _engine.RoundNumber;
            _engine.HostSaysYes(score);

            ScoreChanged?.Invoke(this, mac);
            _ = lastStarted;   // в истории score уже с учётом правки ниже



            AppLogger.Instance.Score($"Ведущий: Да, {score:+#;-#;0} → {mac}");
            
        }

        public void HostSaysNo() 
            {
            AppLogger.Instance.Score("Ведущий: Нет");
            _engine.HostSaysNo();            
            }
        //public void HostSaysNoOne() => _engine.HostSaysNoOne();
        public void HostSaysNoOne()
        {
            AppLogger.Instance.State("Ведущий: Никто не ответил");
            _engine.HostSaysNoOne();
        }

        public void ResetScores()
        {
            _scores.Clear();
            ScoreChanged?.Invoke(this, "*");
        }

        public void ResetGame()
        {
            _scores.Clear();
            _history.Clear();
            _engine.ResetPlayedTracks();
            _engine.SetKnownMacs(Array.Empty<string>());
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        // =============================================================
        // Доступ к счёту
        // =============================================================
        public int GetScore(string mac) =>
            _scores.TryGetValue(mac, out var s) ? s : 0;

        public IReadOnlyDictionary<string, int> AllScores => _scores;

        // =============================================================
        // Обработчики
        // =============================================================
        //private void OnPress(object s, PressEvent p)
        //{
        //    // Запоминаем нажатие как кандидата в победители
        //    _engine.OnPress(p);

        //    // Сообщаем всем окнам — они обновят «кто первый» и подсветку
        //    PressReceived?.Invoke(this, p);
        //}

        private void OnPress(object s, PressEvent p)
        {
            var name = _buttons?.GetByMac(p.Mac)?.Label ?? p.Mac;
            AppLogger.Instance.Press($"🔴 Нажатие: {name}  seq={p.Seq}");
            _engine.OnPress(p);
            PressReceived?.Invoke(this, p);
        }

        private void OnRoundFinished(RoundRecord r)
        {
            _history.Add(r);
            RoundFinished?.Invoke(this, r);
        }

        private void UpdateKnownMacs()
        {
            if (_buttons == null) return;
            var macs = _buttons.GetAll().Select(b => b.Mac).ToList();
            _engine.SetKnownMacs(macs);
        }


        public void ExportRoundsCsv(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Round,Category,Track,WinnerName,WinnerMac,Score,Reason,AnswerSec,Started,Finished");

            foreach (var r in _history)
            {
                sb.Append(r.RoundNumber).Append(',');
                sb.Append(Csv(r.Category)).Append(',');
                sb.Append(Csv(Path.GetFileName(r.TrackFile ?? ""))).Append(',');
                sb.Append(Csv(r.WinnerName ?? "")).Append(',');
                sb.Append(Csv(r.WinnerMac ?? "")).Append(',');
                sb.Append(r.Score).Append(',');
                sb.Append(r.Reason).Append(',');
                sb.Append(r.AnswerTimeSec.ToString("F1",
                    System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                sb.Append(r.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")).Append(',');
                sb.Append(r.FinishedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        /// <summary>Сводная статистика — для UI.</summary>
        public class GameStats
        {
            public int TotalRounds { get; set; }
            public int ScoredRounds { get; set; }
            public int NoOneRounds { get; set; }
            public int AllWrongRounds { get; set; }

            public Dictionary<string, int> WinsByPlayer { get; set; } = new Dictionary<string, int>();
            public Dictionary<string, int> RoundsByCategory { get; set; } = new Dictionary<string, int>();
            public double AverageAnswerSec { get; set; }
        }

        public GameStats GetStats()
        {
            var stats = new GameStats
            {
                TotalRounds = _history.Count,
                ScoredRounds = _history.Count(r => r.Reason == EndReason.Scored && r.Score > 0),
                NoOneRounds = _history.Count(r => r.Reason == EndReason.NoOne),
                AllWrongRounds = _history.Count(r => r.Reason == EndReason.AllAnsweredWrong)
            };

            foreach (var r in _history)
            {
                if (!string.IsNullOrEmpty(r.WinnerName) && r.Score > 0)
                {
                    stats.WinsByPlayer.TryGetValue(r.WinnerName, out var w);
                    stats.WinsByPlayer[r.WinnerName] = w + 1;
                }
                if (!string.IsNullOrEmpty(r.Category))
                {
                    stats.RoundsByCategory.TryGetValue(r.Category, out var c);
                    stats.RoundsByCategory[r.Category] = c + 1;
                }
            }

            var answers = _history.Where(r => r.AnswerTimeSec > 0).Select(r => r.AnswerTimeSec).ToList();
            stats.AverageAnswerSec = answers.Count > 0 ? answers.Average() : 0;

            return stats;
        }

        public void ClearHistory()
        {
            _history.Clear();
            AppLogger.Instance.Info("История раундов очищена.");
        }
    }
}