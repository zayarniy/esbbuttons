using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using GuessMelody.Core;
using GuessMelody.Core.Enums;
using GuessMelody.Core.Models;
using GuessMelody.Core.Serialization;
using GuessMelody.Services;

namespace GuessMelody.ViewModels
{
    public class GameTabViewModel : ViewModelBase
    {
        private readonly LogService _log;
        private readonly GameEngine _engine;
        private readonly DialogService _dlg;
        private readonly RecentFilesService _recent;
        private readonly AudioCoordinator _audio;

        // Ссылки на соседние VM — присваиваются в MainViewModel
        public ButtonsTabViewModel Buttons { get; set; }
        public SettingsTabViewModel Settings { get; set; }
        public FoldersTabViewModel Folders { get; set; }

        public ObservableCollection<string> RecentFiles { get; }
            = new ObservableCollection<string>();

        private string _currentGamePath;

        public GameTabViewModel(
            LogService log,
            GameEngine engine,
            DialogService dlg,
            RecentFilesService recent,
            AudioCoordinator audio)
        {
            _log = log;
            _engine = engine;
            _dlg = dlg;
            _recent = recent;
            _audio = audio;

            StartCommand = new RelayCommand(_ => StartServer(), _ => !IsRunning);
            StopCommand = new RelayCommand(_ => StopServer(), _ => IsRunning);

            LoadGameCommand = new RelayCommand(_ => LoadGame());
            SaveGameCommand = new RelayCommand(_ => SaveGame());
            SaveGameAsCommand = new RelayCommand(_ => SaveGameAs());
            NewGameCommand = new RelayCommand(_ => NewGame());

            OpenRecentCommand = new RelayCommand(p => OpenRecent(p as string));

            OpenPlayerCommand = new RelayCommand(_ => OpenPlayer?.Invoke(this, EventArgs.Empty));
            OpenHostCommand = new RelayCommand(_ => OpenHost?.Invoke(this, EventArgs.Empty));

            TestPressCommand = new RelayCommand(p =>
            {
                if (int.TryParse(p as string, out var id))
                    _engine.EmulatePress(id);
            });
            TestHeartbeatCommand = new RelayCommand(p =>
            {
                if (int.TryParse(p as string, out var id))
                    _engine.EmulateHeartbeat(id);
            });
            TestRoundCommand = new RelayCommand(p =>
            {
                if (int.TryParse(p as string, out var n))
                    _engine.StartRound(n - 1);
            });

            TestScoreYesCommand = new RelayCommand(_ => _engine.SubmitScore(true));
            TestScoreNoCommand = new RelayCommand(_ => _engine.SubmitScore(false));

            ResetScoresCommand = new RelayCommand(_ => ResetScores());
            ResetRoundCommand = new RelayCommand(_ => ResetRound());
            PanicCommand = new RelayCommand(_ => _engine.Panic());
            NextRoundCommand = new RelayCommand(_ => _engine.NextRound());
            FillDemoCommand = new RelayCommand(_ => FillDemo());
            QuickDemoCommand = new RelayCommand(_ => QuickDemo());

            // настройки подтянем позже
            HookEngineEvents();
            RefreshRecentFiles();
        }

        // ---------------------------------------------------------
        // Команды
        // ---------------------------------------------------------
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand LoadGameCommand { get; }
        public ICommand SaveGameCommand { get; }
        public ICommand SaveGameAsCommand { get; }
        public ICommand NewGameCommand { get; }
        public ICommand OpenRecentCommand { get; }
        public ICommand OpenPlayerCommand { get; }
        public ICommand OpenHostCommand { get; }
        public ICommand TestPressCommand { get; }
        public ICommand TestRoundCommand { get; }
        public ICommand TestScoreYesCommand { get; }
        public ICommand TestScoreNoCommand { get; }
        public ICommand ResetScoresCommand { get; }
        public ICommand ResetRoundCommand { get; }
        public ICommand PanicCommand { get; }
        public ICommand NextRoundCommand { get; }
        public ICommand FillDemoCommand { get; }
        public ICommand QuickDemoCommand { get; }

        public ICommand TestHeartbeatCommand { get; }

        public event EventHandler OpenPlayer;
        public event EventHandler OpenHost;

        // ---------------------------------------------------------
        // Свойства
        // ---------------------------------------------------------
        private int _udpPort = 41234;
        public int UdpPort { get => _udpPort; set => Set(ref _udpPort, value); }

        private string _serverIp = "192.168.137.1";
        public string ServerIp { get => _serverIp; set => Set(ref _serverIp, value); }

        private bool _isRunning;
        public bool IsRunning { get => _isRunning; private set => Set(ref _isRunning, value); }

        private string _statusText = "Сервер не запущен";
        public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

        private string _gameName = "Новая игра";
        public string GameName
        {
            get => _gameName;
            set
            {
                if (Set(ref _gameName, value))
                    _engine.Settings.Name = value;
            }
        }

        private string _currentGameInfo = "Игра не загружена";
        public string CurrentGameInfo { get => _currentGameInfo; private set => Set(ref _currentGameInfo, value); }

        public string LocalIpsText =>
            string.Join(", ", GetLocalIPv4().Select(ip => ip.ToString()));

        // ----- Состояние -----
        public string RoundText =>
            _engine.TotalRounds > 0
                ? $"Раунд {_engine.CurrentRound + 1} / {_engine.TotalRounds}"
                : $"Раунд {_engine.CurrentRound + 1}";

        public string StateText
        {
            get
            {
                switch (_engine.State)
                {
                    case RoundState.Idle: return "Ожидание";
                    case RoundState.Countdown: return $"Отсчёт {_engine.CountdownLeftSec:F0}";
                    case RoundState.Playing: return "🎵 Играет фрагмент";
                    case RoundState.WaitingForAnswer:
                        return _engine.FirstPressedTeam != null
                                                        ? $"Ответ: {_engine.FirstPressedTeam.Name}"
                                                        : "Ждём ответ";
                    case RoundState.Scored: return "Очко начислено";
                    case RoundState.Finished: return "Игра завершена";
                }
                return "";
            }
        }

        public string FirstPressedText => _engine.FirstPressedTeam?.Name ?? "—";
        public string CategoryText => _engine.CurrentCategory?.Name ?? "—";
        public string TrackText => _engine.CurrentTrack?.RelativePath ?? "—";

        public string TeamsSummaryText =>
            _engine.Teams.Count == 0
                ? "Игроки не заданы"
                : string.Join("  •  ", _engine.Teams.Select(t => $"{t.Name}: {t.Score}"));

        // ---------------------------------------------------------
        // Engine events
        // ---------------------------------------------------------
        private void HookEngineEvents()
        {
            _engine.StateChanged += (s, e) => RefreshState();
            _engine.ScoreChanged += (s, e) => RefreshState();
            _engine.TrackChanged += (s, e) => RefreshState();
            _engine.SettingsChanged += (s, e) => RefreshState();
            _engine.RemainingChanged += (s, e) => RefreshState();
        }

        private void RefreshState()
        {
            OnPropertyChanged(nameof(RoundText));
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(FirstPressedText));
            OnPropertyChanged(nameof(CategoryText));
            OnPropertyChanged(nameof(TrackText));
            OnPropertyChanged(nameof(TeamsSummaryText));
        }

        // ---------------------------------------------------------
        // Сервер
        // ---------------------------------------------------------
        private void StartServer()
        {
            if (Buttons == null) { _dlg.Warn("Вкладка кнопок не подключена."); return; }
            Buttons.UdpPort = UdpPort;
            Buttons.StartCommand.Execute(null);
            IsRunning = Buttons.IsRunning;
            StatusText = Buttons.StatusText;
            _log.Add(LogKind.System, $"Сервер запущен из вкладки «Игра», UDP:{UdpPort}");
        }

        private void StopServer()
        {
            if (Buttons == null) return;
            Buttons.StopCommand.Execute(null);
            IsRunning = Buttons.IsRunning;
            StatusText = Buttons.StatusText;
        }

        // ---------------------------------------------------------
        // Новая игра
        // ---------------------------------------------------------
        private void NewGame()
        {
            if (!_dlg.Confirm("Начать новую игру? Очки и прогресс будут сброшены.")) return;

            _engine.NewGame();
            _currentGamePath = null;
            CurrentGameInfo = "Новая игра (не сохранена)";
            _log.Add(LogKind.System, "Новая игра.");
        }

        // ---------------------------------------------------------
        // Сбросы
        // ---------------------------------------------------------
        private void ResetScores()
        {
            if (!_dlg.Confirm("Сбросить очки всех игроков?")) return;
            _engine.ResetScores();
        }

        private void ResetRound()
        {
            _engine.Panic();
            _log.Add(LogKind.Game, "Раунд сброшен.");
        }

        // ---------------------------------------------------------
        // Load / Save / Recent
        // ---------------------------------------------------------
        private void LoadGame()
        {
            var path = _dlg.OpenFile(
                "GuessMelody game (*.gmgame)|*.gmgame|JSON (*.json)|*.json|Все файлы (*.*)|*.*");
            if (string.IsNullOrEmpty(path)) return;
            LoadFromFile(path);
        }

        private void OpenRecent(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (!File.Exists(path))
            {
                _dlg.Warn($"Файл не найден: {path}");
                return;
            }
            LoadFromFile(path);
        }

        private void LoadFromFile(string path)
        {
            var s = JsonStore.Load<GameSettings>(path);
            if (s == null) { _dlg.Error("Не удалось прочитать файл."); return; }

            // 1. Обновить модель движка
            _engine.ApplySettings(s);

            // 2. Разложить по вкладкам
            Settings?.ApplyFromSettings(s);
            Folders?.ApplyFromSettings(s);

            // 3. Обновить привязки кнопок (MAC↔имя) — забираем команды из s.Buttons
            SyncTeamsToButtons(s);

            // 4. AudioCoordinator: корневая папка
            _audio.RootFolder = s.Folders?.RootFolder ?? "";

            // 5. Сбросить игровое состояние — загрузка не должна приносить очки
            _engine.NewGame();

            // 6. Имя игры
            _gameName = s.Name ?? "Игра";
            OnPropertyChanged(nameof(GameName));
            _engine.Settings.Name = _gameName;

            // 7. Файл и история
            _currentGamePath = path;
            CurrentGameInfo = $"{Path.GetFileName(path)}  ({File.GetLastWriteTime(path):dd.MM.yyyy HH:mm})";
            _recent.Add(path);
            RefreshRecentFiles();

            _log.Add(LogKind.System, $"Игра загружена: {path}");
            RefreshState();
        }

        private void SaveGame()
        {
            if (string.IsNullOrEmpty(_currentGamePath))
            {
                SaveGameAs();
                return;
            }
            SaveToFile(_currentGamePath);
        }

        private void SaveGameAs()
        {
            var defaultName = string.IsNullOrEmpty(GameName)
                ? "game.gmgame"
                : MakeSafeFileName(GameName) + ".gmgame";

            var path = _dlg.SaveFile("GuessMelody game (*.gmgame)|*.gmgame", defaultName);
            if (string.IsNullOrEmpty(path)) return;
            SaveToFile(path);
        }

        private void SaveToFile(string path)
        {
            var s = _engine.Settings;

            // 1. Имя игры
            s.Name = GameName;
            s.CreatedUtc = DateTime.UtcNow.ToString("o");

            // 2. Снять текущие настройки со вкладок
            Settings?.ApplyToSettings(s);
            Folders?.ApplyToSettings(s);

            // 3. Синхронизировать команды с привязками кнопок (MAC↔имя)
            SyncTeamsFromButtons(s);

            // 4. Записать
            if (JsonStore.Save(path, s))
            {
                _currentGamePath = path;
                CurrentGameInfo = $"{Path.GetFileName(path)}  ({File.GetLastWriteTime(path):dd.MM.yyyy HH:mm})";
                _recent.Add(path);
                RefreshRecentFiles();
                _log.Add(LogKind.System, $"Игра сохранена: {path}");
            }
            else
            {
                _dlg.Error("Не удалось сохранить игру.");
            }
        }

        private void RefreshRecentFiles()
        {
            RecentFiles.Clear();
            foreach (var f in _recent.Files) RecentFiles.Add(f);
        }

        // ---------------------------------------------------------
        // Синхронизация команд ↔ привязок кнопок
        // ---------------------------------------------------------
        /// <summary>Забрать Teams из GameSettings и разложить их по привязкам ButtonsTab.</summary>
        private void SyncTeamsToButtons(GameSettings s)
        {
            if (Buttons == null || s.Buttons?.Teams == null) return;

            // Создаём BindingsConfig из s.Buttons.Teams
            var cfg = new ButtonsConfig();
            foreach (var t in s.Buttons.Teams)
            {
                cfg.Bindings.Add(new ButtonBinding
                {
                    TeamId = t.Id,
                    TeamName = t.Name,
                    Mac = t.Mac
                });
            }
           
        }

        /// <summary>Перенести текущие привязки из ButtonsTab в GameSettings.</summary>
        private void SyncTeamsFromButtons(GameSettings s)
        {
            if (Buttons == null || s == null) return;
            if (s.Buttons == null) s.Buttons = new ButtonsMap();

            // Соберём из Rows ButtonsTab (там есть Name и Mac)
            var teams = new System.Collections.Generic.List<Team>();
            int id = 1;
            foreach (var row in Buttons.Rows)
            {
                teams.Add(new Team
                {
                    Id = id++,
                    Name = string.IsNullOrWhiteSpace(row.Name) ? row.Mac : row.Name,
                    Mac = row.Mac,
                    Score = 0
                });
            }
            s.Buttons.Teams = teams;
        }

        // ---------------------------------------------------------
        // Быстрые сценарии
        // ---------------------------------------------------------
        private void FillDemo()
        {
            var s = _engine.Settings;

            if (s.Buttons?.Teams == null || s.Buttons.Teams.Count == 0)
            {
                if (s.Buttons == null) s.Buttons = new ButtonsMap();
                for (int i = 1; i <= 3; i++)
                    _engine.EnsureTeam(i, $"Игрок {i}");
            }

            if (s.Folders?.Categories == null || s.Folders.Categories.Count == 0)
            {
                if (s.Folders == null) s.Folders = new FoldersCatalog();
                for (int i = 1; i <= 3; i++)
                {
                    var cat = new Category { Name = $"Категория {i}" };
                    for (int j = 1; j <= 3; j++)
                        cat.Tracks.Add(new Track { RelativePath = $"demo/cat{i}/track{j}.mp3" });
                    s.Folders.Categories.Add(cat);
                }
                _log.Add(LogKind.System, "Заполнены демо-категории (без реальных файлов).");
            }

            _dlg.Info("Заполнено: 3 игрока (VIRT), 3 категории по 3 трека.\n" +
                      "Открыты окна Player и Host.");

            OpenPlayer?.Invoke(this, EventArgs.Empty);
            OpenHost?.Invoke(this, EventArgs.Empty);
        }

        private void QuickDemo()
        {
            FillDemo();
            _engine.StartRound(0);
        }

        // ---------------------------------------------------------
        // Вспомогательное
        // ---------------------------------------------------------
        private static System.Net.IPAddress[] GetLocalIPv4()
        {
            try
            {
                return System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName())
                    .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .ToArray();
            }
            catch { return new System.Net.IPAddress[0]; }
        }

        private static string MakeSafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Where(c => !invalid.Contains(c)).ToArray());
        }

        public GameSettings GetSettingsSnapshotForAutosave()
        {
            var s = _engine.Settings;
            s.Name = GameName;
            Settings?.ApplyToSettings(s);
            Folders?.ApplyToSettings(s);
            SyncTeamsFromButtons(s);
            return s;
        }
    }
}