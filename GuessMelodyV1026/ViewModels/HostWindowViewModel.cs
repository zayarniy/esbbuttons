using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using GuessMelody.Core;
using GuessMelody.Core.Enums;
using GuessMelody.Services;

namespace GuessMelody.ViewModels
{
    public class HostWindowViewModel : ViewModelBase, IDisposable
    {
        private readonly GameEngine _engine;
        private readonly AudioCoordinator _audio;

        public ObservableCollection<TeamTile> Teams { get; } = new ObservableCollection<TeamTile>();
        public ObservableCollection<CategoryTile> Categories { get; } = new ObservableCollection<CategoryTile>();

        public HostWindowViewModel(GameEngine engine, AudioCoordinator audio)
        {
            _engine = engine;
            _audio = audio;

            _engine.StateChanged += (s, e) => RefreshAll();
            _engine.ScoreChanged += (s, e) => RefreshTeams();
            _engine.TrackChanged += (s, e) => RefreshAll();
            _engine.RemainingChanged += (s, e) => RebuildCategories();
            _engine.CategoryHighlighted += (s, cat) => MarkActiveCategory(cat);

            StartRoundCommand = new RelayCommand(_ => StartNextRound());
            NextRoundCommand = new RelayCommand(_ => { _engine.NextRound(); RebuildCategories(); });
            EndRoundCommand = new RelayCommand(_ => _engine.EndRound());
            PauseCommand = new RelayCommand(_ => _engine.PauseMusic());
            ResumeCommand = new RelayCommand(_ => _engine.ResumeMusic());
            YesCommand = new RelayCommand(_ => { _engine.SubmitScore(true); _audio?.PlayCorrect(); });
            NoCommand = new RelayCommand(_ => { _engine.SubmitScore(false); _audio?.PlayWrong(); });
            PanicCommand = new RelayCommand(_ => _engine.Panic());
            _engine.Heartbeat += (s, team) =>
            {
                if (team == null) return;
                var tile = Teams.FirstOrDefault(x => x.Team.Mac == team.Mac);
                tile?.PulseHeartbeat();
            };
            _engine.HeartbeatChanged += (s, e) =>
            {
                foreach (var t in Teams) t.Refresh();
            };
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

        public void Dispose() { }
    }
}