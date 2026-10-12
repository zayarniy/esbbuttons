using GuessMelody.Core;
using GuessMelody.Core.Enums;
using GuessMelody.Core.Models;
using GuessMelody.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace GuessMelody.ViewModels
{
    public class HostWindowViewModel : ViewModelBase, IDisposable
    {
        private readonly GameEngine _engine;
        private readonly AudioCoordinator _audio;
        private readonly FeedbackService _feedback;
        public ObservableCollection<TeamTile> Teams { get; } = new ObservableCollection<TeamTile>();
        public ObservableCollection<CategoryTile> Categories { get; } = new ObservableCollection<CategoryTile>();
        private readonly EventHandler _hStateChanged;
        private readonly EventHandler _hScoreChanged;
        private readonly EventHandler<Track> _hTrackChanged;
        private readonly EventHandler _hRemainingChanged;
        private readonly EventHandler<Category> _hCategoryHighlighted;
        private readonly EventHandler<Team> _hHeartbeat;
        private readonly EventHandler _hHeartbeatChanged;
        private readonly EventHandler _hTeamsChanged;
        private readonly EventHandler<string> _hCorrectRequested;
        private readonly EventHandler<string> _hWrongRequested;
        private readonly EventHandler _hWrongPauseFinished;
        private readonly EventHandler<string> _hRevealRequested;


        public HostWindowViewModel(GameEngine engine, AudioCoordinator audio, FeedbackService feedback)
        {
            _engine = engine;
            _audio = audio;
            _feedback = feedback;

            // ---- Делегаты-обработчики (сохраняем в поля для Dispose) ----
            _hStateChanged = (s, e) => RefreshAll();
            _hScoreChanged = (s, e) => RefreshTeams();
            _hTrackChanged = (s, t) => RefreshAll();
            _hRemainingChanged = (s, e) => RebuildCategories();
            _hCategoryHighlighted = (s, cat) => MarkActiveCategory(cat);
            _hTeamsChanged = (s, e) => RefreshTeams();

            _hHeartbeat = (s, team) =>
            {
                if (team == null) return;
                var tile = Teams.FirstOrDefault(x =>
                    string.Equals(x.Team.Mac, team.Mac, StringComparison.OrdinalIgnoreCase));
                if (tile == null) { RefreshTeams(); return; }
                tile.PulseHeartbeat();
            };

            _hHeartbeatChanged = (s, e) =>
            {
                foreach (var t in Teams) t.Refresh();
            };

            _hCorrectRequested = (s, path) => _feedback.ShowCorrect(path);
            _hWrongRequested = (s, name) =>
                _feedback.ShowWrong(_engine.Settings?.Timings?.WrongFeedbackPauseSec ?? 3);
            _hRevealRequested = (s, path) => _feedback.RevealCorrect(path);

            _hWrongPauseFinished = (s, e) =>
            {
                if (_engine.State == RoundState.Wrong &&
                    _engine.Settings?.Rules?.AllowRePressAfterWrong == true)
                {
                    _engine.ResumeMusic();
                }
            };

            // ---- Подписки (ровно один раз) ----
            _engine.StateChanged += _hStateChanged;
            _engine.ScoreChanged += _hScoreChanged;
            _engine.TrackChanged += _hTrackChanged;
            _engine.RemainingChanged += _hRemainingChanged;
            _engine.CategoryHighlighted += _hCategoryHighlighted;
            _engine.Heartbeat += _hHeartbeat;
            _engine.HeartbeatChanged += _hHeartbeatChanged;
            _engine.TeamsChanged += _hTeamsChanged;
            _engine.CorrectAnswerRequested += _hCorrectRequested;
            _engine.WrongAnswerRequested += _hWrongRequested;
            _engine.RevealAnswerRequested += _hRevealRequested;
            _feedback.WrongPauseFinished += _hWrongPauseFinished;

            // ---- Команды ----
            StartRoundCommand = new RelayCommand(_ => StartNextRound());
            NextRoundCommand = new RelayCommand(_ => { _engine.NextRound(); RebuildCategories(); });
            EndRoundCommand = new RelayCommand(_ => _engine.EndRound());
            PauseCommand = new RelayCommand(_ => _engine.PauseMusic());
            ResumeCommand = new RelayCommand(_ => _engine.ResumeMusic());

            RevealAnswerCommand = new RelayCommand(
                _ => _engine.RevealAnswer(),
                _ => _engine.CurrentTrack != null);

            // Звуки «Правильный»/«Неправильный» играет FeedbackService — в командах не дублируем
            YesCommand = new RelayCommand(_ => _engine.SubmitScore(true));
            NoCommand = new RelayCommand(_ => _engine.SubmitScore(false));
            PanicCommand = new RelayCommand(_ => _engine.Panic());

            RebuildCategories();
            RefreshAll();
        }

        public ICommand StartRoundCommand { get; }
        public ICommand NextRoundCommand { get; }
        public ICommand EndRoundCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand ResumeCommand { get; }
        public ICommand YesCommand { get; }
        public ICommand NoCommand { get; }
        public ICommand PanicCommand { get; }

        public ICommand RevealAnswerCommand { get; }

        public string RoundText =>
            _engine.TotalRounds > 0
                ? $"Раунд {_engine.CurrentRound + 1} / {_engine.TotalRounds}"
                : $"Раунд {_engine.CurrentRound + 1}";

        public string StateText => new Converters.RoundStateToText()
            .Convert(_engine.State, typeof(string), null, System.Globalization.CultureInfo.CurrentCulture)
            .ToString();

        public string CategoryText => _engine.CurrentCategory?.Name ?? "—";
        public string TrackText => _engine.CurrentTrack?.RelativePath ?? "—";
        public string FirstPressedText => _engine.FirstPressedTeam?.Name ?? "—";

        public string TrackTimeText =>
            _engine.TrackTotalSec > 0
                ? $"{_engine.TrackLeftSec:F0} / {_engine.TrackTotalSec:F0} сек"
                : "—";

        public double TrackProgress =>
            _engine.TrackTotalSec > 0
                ? Math.Max(0, Math.Min(1, _engine.TrackLeftSec / _engine.TrackTotalSec))
                : 0;

        public void RefreshAll()
        {
            OnPropertyChanged(nameof(RoundText));
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(CategoryText));
            OnPropertyChanged(nameof(TrackText));
            OnPropertyChanged(nameof(FirstPressedText));
            OnPropertyChanged(nameof(TrackTimeText));
            OnPropertyChanged(nameof(TrackProgress));
            RefreshTeams();
        }

        private void RefreshTeams()
        {
            Teams.Clear();
            foreach (var t in _engine.Teams)
                Teams.Add(new TeamTile(t));
            
        }

        public void RebuildCategories()
        {
            var selected = SelectedCategory;
            Categories.Clear();

            var s = _engine.Settings;
            if (s?.Folders?.Categories == null) return;

            for (int i = 0; i < s.Folders.Categories.Count; i++)
            {
                var c = s.Folders.Categories[i];
                int total = c.Tracks?.Count ?? 0;
                int remaining = _engine.GetRemaining(i);
                var tile = new CategoryTile(i, c.Name, total, remaining);
                if (_engine.CurrentCategory != null &&
                    string.Equals(c.Name, _engine.CurrentCategory.Name, StringComparison.OrdinalIgnoreCase))
                {
                    tile.IsActive = true;
                }
                Categories.Add(tile);
            }

            if (selected >= 0 && selected < Categories.Count)
                SelectedCategory = selected;
        }

        private void MarkActiveCategory(Core.Models.Category cat)
        {
            foreach (var c in Categories)
                c.IsActive = string.Equals(c.Name, cat?.Name, StringComparison.OrdinalIgnoreCase);
        }

        private int _selectedCategory = 0;
        public int SelectedCategory
        {
            get => _selectedCategory;
            set => Set(ref _selectedCategory, value);
        }

        private void StartNextRound()
        {
            if (_engine.State == RoundState.Idle || _engine.State == RoundState.Scored)
            {
                _engine.StartRound(SelectedCategory);
            }
            else
            {
                _engine.NextRound();
                _engine.StartRound(SelectedCategory);
            }
        }

        public void Dispose() {

            try
            {
                _engine.StateChanged -= _hStateChanged;
                _engine.ScoreChanged -= _hScoreChanged;
                _engine.TrackChanged -= _hTrackChanged;
                _engine.RemainingChanged -= _hRemainingChanged;
                _engine.CategoryHighlighted -= _hCategoryHighlighted;
                _engine.Heartbeat -= _hHeartbeat;
                _engine.HeartbeatChanged -= _hHeartbeatChanged;
                _engine.TeamsChanged -= _hTeamsChanged;
                _engine.CorrectAnswerRequested -= _hCorrectRequested;
                _engine.WrongAnswerRequested -= _hWrongRequested;
                _feedback.WrongPauseFinished -= _hWrongPauseFinished;
                _engine.RevealAnswerRequested -= _hRevealRequested;
            }
            catch { }
        }
    }
}