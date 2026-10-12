using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GuessMelody.Core;
using GuessMelody.Core.Enums;
using GuessMelody.Core.Models;
using GuessMelody.Services;

namespace GuessMelody.ViewModels
{
    public class CategoryTile : ViewModelBase
    {
        public string Name { get; }
        public int Total { get; }
        public int Index { get; }

        private int _remaining;
        public int Remaining
        {
            get => _remaining;
            set
            {
                if (Set(ref _remaining, value))
                    OnPropertyChanged(nameof(CountText));
            }
        }

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set => Set(ref _isActive, value);
        }

        private bool _isBlinking;
        public bool IsBlinking
        {
            get => _isBlinking;
            set => Set(ref _isBlinking, value);
        }

        public string CountText => $"{Remaining} / {Total}";

        public CategoryTile(int index, string name, int total, int remaining)
        {
            Index = index;
            Name = name;
            Total = total;
            Remaining = remaining;
        }
    }

    public class TeamTile : ViewModelBase
    {
        public Team Team { get; }

        private int _heartbeatPulse = -1;
        public int HeartbeatPulse
        {
            get => _heartbeatPulse;
            set => Set(ref _heartbeatPulse, value);
        }

        private static readonly DispatcherTimer _globalPulseTimer = CreateGlobalTimer();
        private static readonly System.Collections.Generic.List<TeamTile> _pulsingTiles =
            new System.Collections.Generic.List<TeamTile>();

        private static DispatcherTimer CreateGlobalTimer()
        {
            var t = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(300) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                foreach (var tile in _pulsingTiles.ToArray())
                    tile.IsPulsing = false;
                _pulsingTiles.Clear();
            };
            return t;
        }

        private bool _isFlashing;
        public bool IsFlashing
        {
            get => _isFlashing;
            set => Set(ref _isFlashing, value);
        }

        private bool _isPulsing;
        public bool IsPulsing
        {
            get => _isPulsing;
            set => Set(ref _isPulsing, value);
        }

        public string Name => Team.Name;
        public int Score => Team.Score;
        public bool IsAlive => Team.IsAlive;
        public string AliveGlyph => Team.IsAlive ? "●" : "○";

        public TeamTile(Team team) { Team = team; }

        public void Refresh()
        {
            OnPropertyChanged(nameof(Score));
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(IsAlive));
            OnPropertyChanged(nameof(AliveGlyph));
        }

        public void PulseHeartbeat()
        {
            IsPulsing = false;              // принудительно сброс — гарантирует, что DataTrigger снова сработает
            if (!_pulsingTiles.Contains(this)) _pulsingTiles.Add(this);
            IsPulsing = true;               // → EnterActions в XAML
            _globalPulseTimer.Stop();
            _globalPulseTimer.Start();

            OnPropertyChanged(nameof(IsAlive));
            OnPropertyChanged(nameof(AliveGlyph));
        }
    }

    public class PlayerWindowViewModel : ViewModelBase, IDisposable
    {
        private readonly GameEngine _engine;
        private readonly AudioCoordinator _audio;
        private readonly DispatcherTimer _flashTimer;
        private readonly DispatcherTimer _blinkTimer;

        // Сохраняем делегаты в поля — так сможем отписаться в Dispose
        private readonly EventHandler _hStateChanged;
        private readonly EventHandler _hScoreChanged;
        private readonly EventHandler<Track> _hTrackChanged;
        private readonly EventHandler<Team> _hFirstPressed;
        private readonly EventHandler<Category> _hCategoryHighlighted;
        private readonly EventHandler _hRemainingChanged;
        private readonly EventHandler _hSettingsChanged;
        private readonly EventHandler<Team> _hHeartbeat;
        private readonly EventHandler _hHeartbeatChanged;
        private readonly EventHandler _hTeamsChanged;

        private bool _blinkPhase;
        private CategoryTile _blinkingTile;

        public ObservableCollection<CategoryTile> Categories { get; } = new ObservableCollection<CategoryTile>();
        public ObservableCollection<TeamTile> Teams { get; } = new ObservableCollection<TeamTile>();

        public PlayerWindowViewModel(GameEngine engine, AudioCoordinator audio)
        {
            _engine = engine;
            _audio = audio;

            _hStateChanged = (s, e) => OnEngineStateChanged();
            _hScoreChanged = (s, e) => OnEngineScoreChanged();
            _hTrackChanged = (s, t) => OnEngineTrackChanged(t);
            _hFirstPressed = (s, team) => OnFirstPressed(team);
            _hCategoryHighlighted = (s, cat) => OnCategoryHighlighted(cat);
            _hRemainingChanged = (s, e) => OnRemainingChanged();
            _hSettingsChanged = (s, e) => OnSettingsChanged();
            _hHeartbeat = (s, team) => OnTeamHeartbeat(team);
            _hHeartbeatChanged = (s, e) => OnHeartbeatChanged();
            _hTeamsChanged = (s, e) => RebuildFromSettings();

            _engine.StateChanged += _hStateChanged;
            _engine.ScoreChanged += _hScoreChanged;
            _engine.TrackChanged += _hTrackChanged;
            _engine.FirstPressed += _hFirstPressed;
            _engine.CategoryHighlighted += _hCategoryHighlighted;
            _engine.RemainingChanged += _hRemainingChanged;
            _engine.SettingsChanged += _hSettingsChanged;
            _engine.Heartbeat += _hHeartbeat;
            _engine.HeartbeatChanged += _hHeartbeatChanged;
            _engine.TeamsChanged += _hTeamsChanged;

            FlashCommand = new RelayCommand(p => Adjust(p as string, 1));
            UnflashCommand = new RelayCommand(p => Adjust(p as string, -1));

            _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _flashTimer.Tick += (_, __) =>
            {
                foreach (var t in Teams) t.IsFlashing = false;
                _flashTimer.Stop();
            };

            // Мигание выбранной категории — 8 фаз по 250 мс = 2 секунды
            _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _blinkTimer.Tick += (_, __) => BlinkTick();

            RebuildFromSettings();
        }

        public ICommand FlashCommand { get; }
        public ICommand UnflashCommand { get; }

        public string GameTitle => _engine.Settings?.Name ?? "Угадай мелодию";

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
                    case RoundState.Idle:
                        return "Ожидание";
                    case RoundState.Countdown:
                        return $"Отсчёт: {_engine.CountdownLeftSec:F0}";
                    case RoundState.Playing:
                        return _engine.FirstPressedTeam != null
                            ? $"Ответ: {_engine.FirstPressedTeam.Name}"
                            : "🎵 Играет фрагмент";
                    case RoundState.WaitingForAnswer:
                        return _engine.FirstPressedTeam != null
                            ? $"⏸ Пауза — ответ: {_engine.FirstPressedTeam.Name}"
                            : "⏸ Пауза";
                    case RoundState.Wrong:
                        return "❌ Неверный ответ";
                    case RoundState.Scored:
                        return "✅ Очко начислено";
                    case RoundState.Finished:
                        return "Игра завершена";
                    default:
                        return "";
                }
            }
        }

        public double CountdownLeft => _engine.CountdownLeftSec;
        public double AnswerLeft => _engine.AnswerLeftSec;
        public double TrackLeft => _engine.TrackLeftSec;
        public double TrackTotal => _engine.TrackTotalSec;

        public string PlayerBgColor => _engine.Settings?.Appearance?.PlayerBgColor ?? "#1E1E2E";
        public string AccentColor => _engine.Settings?.Appearance?.PlayerAccentColor ?? "#FFD166";
        public string TextColor => _engine.Settings?.Appearance?.PlayerTextColor ?? "#EEEEEE";

        public int CategoryFontSize => _engine.Settings?.Appearance?.CategoryFontSize ?? 32;
        public int NameFontSize => _engine.Settings?.Appearance?.PlayerNameFontSize ?? 28;
        public int ScoreFontSize => _engine.Settings?.Appearance?.PlayerScoreFontSize ?? 40;

        public Brush BackgroundBrush => ParseBrush(PlayerBgColor);
        public Brush AccentBrush => ParseBrush(AccentColor);
        public Brush TextBrush => ParseBrush(TextColor);

        private static Brush ParseBrush(string hex)
        {
            try { return (Brush)new BrushConverter().ConvertFromString(hex); }
            catch { return Brushes.Black; }
        }

        // ---------------------------------------------------------
        public double TrackRemainFraction
        {
            get
            {
                var total = _engine.TrackTotalSec;
                if (total <= 0) return 0;
                var left = _engine.TrackLeftSec;
                if (left < 0) left = 0;
                if (left > total) left = total;
                return left / total;   // 1.0 → 0.0 по мере проигрывания
            }
        }

        private void OnSettingsChanged()
        {
            OnPropertyChanged(nameof(PlayerBgColor));
            OnPropertyChanged(nameof(AccentColor));
            OnPropertyChanged(nameof(TextColor));
            OnPropertyChanged(nameof(BackgroundBrush));
            OnPropertyChanged(nameof(AccentBrush));
            OnPropertyChanged(nameof(TextBrush));
            OnPropertyChanged(nameof(CategoryFontSize));
            OnPropertyChanged(nameof(NameFontSize));
            OnPropertyChanged(nameof(ScoreFontSize));
            RebuildFromSettings();
        }
        public void RebuildFromSettings()
        {
            Categories.Clear();
            Teams.Clear();

            var s = _engine.Settings;

            if (s?.Folders?.Categories != null)
            {
                for (int i = 0; i < s.Folders.Categories.Count; i++)
                {
                    var c = s.Folders.Categories[i];
                    int total = c.Tracks?.Count ?? 0;
                    int remaining = _engine.GetRemaining(i);
                    Categories.Add(new CategoryTile(i, c.Name, total, remaining));
                }
            }

            if (s?.Buttons?.Teams != null)
            {
                foreach (var t in s.Buttons.Teams)
                    Teams.Add(new TeamTile(t));
            }

            OnPropertyChanged(nameof(GameTitle));
            OnPropertyChanged(nameof(RoundText));
            OnPropertyChanged(nameof(PlayerBgColor));
            OnPropertyChanged(nameof(AccentColor));
            OnPropertyChanged(nameof(TextColor));
            OnPropertyChanged(nameof(BackgroundBrush));
            OnPropertyChanged(nameof(AccentBrush));
            OnPropertyChanged(nameof(TextBrush));
            OnPropertyChanged(nameof(CategoryFontSize));
            OnPropertyChanged(nameof(NameFontSize));
            OnPropertyChanged(nameof(ScoreFontSize));
        }

        // ---------------------------------------------------------
        // Реакции
        // ---------------------------------------------------------
        private void OnEngineStateChanged()
        {
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(RoundText));
            OnPropertyChanged(nameof(CountdownLeft));
            OnPropertyChanged(nameof(AnswerLeft));
            OnPropertyChanged(nameof(TrackLeft));
            OnPropertyChanged(nameof(TrackTotal));
            OnPropertyChanged(nameof(TrackRemainFraction));
        }

        private void OnEngineScoreChanged()
        {
            foreach (var t in Teams) t.Refresh();
        }

        private void OnEngineTrackChanged(Track t)
        {
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(TrackTotal));
        }

        private void OnFirstPressed(Team team)
        {
            if (team == null) return;
            var tile = Teams.FirstOrDefault(x => x.Team.Mac == team.Mac);
            if (tile != null)
            {
                tile.IsFlashing = true;
                _flashTimer.Stop();
                _flashTimer.Start();
            }
            _audio?.PlayBuzzer();
        }

        /// <summary>Новая категория выбрана — запускаем мигание.</summary>
        private void OnCategoryHighlighted(Category cat)
        {
            if (cat == null) return;

            // Снимаем активность у всех
            foreach (var c in Categories)
            {
                c.IsActive = false;
                c.IsBlinking = false;
            }

            _blinkingTile = Categories.FirstOrDefault(c =>
                string.Equals(c.Name, cat.Name, StringComparison.OrdinalIgnoreCase));

            if (_blinkingTile == null) return;

            _blinkingTile.IsActive = true;
            _blinkingTile.IsBlinking = true;
            _blinkPhase = true;
            _blinkTimer.Stop();
            _blinkTimer.Start();
        }

        private int _blinkTicks;

        private void BlinkTick()
        {
            if (_blinkingTile == null) { _blinkTimer.Stop(); return; }

            _blinkTicks++;
            _blinkPhase = !_blinkPhase;
            _blinkingTile.IsBlinking = _blinkPhase;

            // 8 фаз по 250 мс = 2 с, затем плавно оставляем только активный цвет
            if (_blinkTicks >= 8)
            {
                _blinkTimer.Stop();
                _blinkingTile.IsBlinking = false;
                _blinkTicks = 0;
            }
        }

        /// <summary>Пересчёт оставшихся треков в категориях.</summary>
        private void OnRemainingChanged()
        {
            for (int i = 0; i < Categories.Count; i++)
            {
                var tile = Categories[i];
                tile.Remaining = _engine.GetRemaining(tile.Index);
            }
        }

        private void Adjust(string teamName, int delta)
        {
            var t = Teams.FirstOrDefault(x => x.Name == teamName);
            if (t == null) return;
            _engine.AdjustScore(t.Team.Mac, delta);
        }

        public void Dispose()
        {
            try
            {
                _engine.StateChanged -= _hStateChanged;
                _engine.ScoreChanged -= _hScoreChanged;
                _engine.TrackChanged -= _hTrackChanged;
                _engine.FirstPressed -= _hFirstPressed;
                _engine.CategoryHighlighted -= _hCategoryHighlighted;
                _engine.RemainingChanged -= _hRemainingChanged;
                _engine.SettingsChanged -= _hSettingsChanged;
                _engine.Heartbeat -= _hHeartbeat;
                _engine.HeartbeatChanged -= _hHeartbeatChanged;
                _engine.TeamsChanged -= _hTeamsChanged;
            }
            catch { }

            _flashTimer?.Stop();
            _blinkTimer?.Stop();
        }

        private void OnTeamHeartbeat(Team team)
        {
            if (team == null) return;

            var tile = Teams.FirstOrDefault(x =>
                string.Equals(x.Team.Mac, team.Mac, StringComparison.OrdinalIgnoreCase));

            if (tile == null)
            {
                // Команда появилась в движке, но в UI её ещё нет.
                // Аккуратно пересобираем коллекцию плиток — тогда плитка появится,
                // но с HeartbeatPulse = -1 и без анимации.
                RebuildFromSettings();
                return;
            }

            tile.PulseHeartbeat();
        }
        private void OnHeartbeatChanged()
        {
            foreach (var t in Teams) t.Refresh();
        }
    }
}