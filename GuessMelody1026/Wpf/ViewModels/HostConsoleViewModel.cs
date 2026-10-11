using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.RightsManagement;
using System.Windows.Threading;
using GuessMelody.Core.Audio;
using GuessMelody.Core.Models;
using GuessMelody.Wpf.Game;

namespace GuessMelody.Wpf.ViewModels
{
    public class PressRow
    {
        public int Order { get; set; }
        public string Mac { get; set; }
        public string Name { get; set; }
        public string TimeText { get; set; }   // HH:mm:ss.fff
        public bool IsWinner { get; set; }
    }

    public class HostConsoleViewModel : INotifyPropertyChanged
    {
        private readonly GameController _gc = GameController.Instance;
        private readonly IAudioEngine _audio;

        public ObservableCollection<PressRow> Presses { get; } = new ObservableCollection<PressRow>();

        // ---------- Плеер ----------
        private string _trackName = "";
        public string TrackName { get => _trackName; set { _trackName = value; OnPropertyChanged(); } }

        private string _timeText = "0:00 / 0:00";
        public string TimeText { get => _timeText; set { _timeText = value; OnPropertyChanged(); } }

        private double _progress;      // 0..1000
        public double Progress { get => _progress; set { _progress = value; OnPropertyChanged(); } }

        private bool _isPlaying;
        public bool IsPlaying { get => _isPlaying; set { _isPlaying = value; OnPropertyChanged(); } }

        // ---------- Раунд / состояние ----------
        private string _stateText = "Ожидание";
        public string StateText { get => _stateText; set { _stateText = value; OnPropertyChanged(); } }

        private string _categoryName = "";
        public string CategoryName { get => _categoryName; set { _categoryName = value; OnPropertyChanged(); } }

        private int _roundNumber;
        public int RoundNumber { get => _roundNumber; set { _roundNumber = value; OnPropertyChanged(); } }

        // ---------- Кто отвечает ----------
        private string _winnerName = "";
        public string WinnerName { get => _winnerName; set { _winnerName = value; OnPropertyChanged(); } }

        private int _answerSecondsLeft;
        public int AnswerSecondsLeft { get => _answerSecondsLeft; set { _answerSecondsLeft = value; OnPropertyChanged(); } }

        private bool _answerActive;
        public bool AnswerActive { get => _answerActive; set { _answerActive = value; OnPropertyChanged(); } }

        // ---------- Зависимости ----------
        private DispatcherTimer _uiTimer;

        public HostConsoleViewModel(IAudioEngine audio)
        {
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));

            _gc.StateChanged += (s, e) => RefreshFromEngine();
            _gc.PressReceived += (s, p) => OnPress(p);
            _gc.RoundFinished += (s, r) => OnRoundFinished(r);
            _gc.Message += (s, m) => { /* можно показать в логе */ };

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _uiTimer.Tick += (_, __) => Tick();
            _uiTimer.Start();
        }

        // =============================================================
        // Управление плеером
        // =============================================================
        public void Play() => _audio.Play();
        public void Pause() => _audio.Pause();
        public void Stop()
        {
            _audio.Stop();
            Presses.Clear();
            WinnerName = "";
            AnswerActive = false;
        }

        public void SeekRelative(double seconds)
        {
            var pos = _audio.Position + TimeSpan.FromSeconds(seconds);
            _audio.Seek(pos);
        }

        public double Volume
        {
            get => _audio.Volume;
            set { _audio.Volume = (float)value; OnPropertyChanged(); }
        }

        // =============================================================
        // Команды ведущего
        // =============================================================
        public void StartRound(int categoryIndex)
        {
            Presses.Clear();
            WinnerName = "";
            _gc.StartRound(categoryIndex);
            RefreshFromEngine();
        }

        public void HostSaysYes(int score)
        {
            _gc.HostSaysYes(score);
            AnswerActive = false;
            RefreshFromEngine();
        }

        public void HostSaysNo()
        {
            _gc.HostSaysNo();
            AnswerActive = false;
            RefreshFromEngine();
        }

        public void HostSaysNoOne()
        {
            _gc.HostSaysNoOne();
            AnswerActive = false;
            RefreshFromEngine();
        }

        public void ResetGame() => _gc.ResetGame();
        public void ResetScores() => _gc.ResetScores();

        // =============================================================
        // Обновление из Engine
        // =============================================================
        public void RefreshFromEngine()
        {
            var e = _gc.Engine;
            if (e == null) return;

            RoundNumber = e.RoundNumber;
            CategoryName = e.Category?.Name ?? "";
            TrackName = e.TrackFile ?? "";
            StateText = e.State.ToString();

            switch (e.State)
            {
                case RoundState.WaitingAnswer:
                    AnswerActive = true;
                    AnswerSecondsLeft = (int)Math.Ceiling(e.TimeLeftForAnswer.TotalSeconds);
                    WinnerName = GetDisplayName(e.CurrentWinner?.Mac);
                    break;

                case RoundState.TrackPlaying:
                case RoundState.Countdown:
                case RoundState.RoundStart:
                    AnswerActive = false;
                    WinnerName = "";
                    break;

                case RoundState.Idle:
                case RoundState.ScoreApplied:
                    AnswerActive = false;
                    break;
            }
        }

        private void OnPress(PressEvent p)
        {
            var display = GetDisplayName(p.Mac);
            var row = new PressRow
            {
                Order = Presses.Count + 1,
                Mac = p.Mac,
                Name = display,
                TimeText = p.ReceivedUtc.ToLocalTime().ToString("HH:mm:ss.fff"),
                IsWinner = false
            };
            Presses.Add(row);

            // Первое нажатие — победитель
            if (Presses.Count == 1)
            {
                row.IsWinner = true;
                WinnerName = display;
            }
        }

        private void OnRoundFinished(RoundRecord r)
        {
            AnswerActive = false;
            // Пометим победителя в списке, если есть
            if (r.WinnerMac != null)
            {
                var row = Presses.FirstOrDefault(p =>
                    string.Equals(p.Mac, r.WinnerMac, StringComparison.OrdinalIgnoreCase));
                if (row != null) row.IsWinner = true;
            }
        }

        private void Tick()
        {
            // Позиция трека
            var pos = _audio.Position;
            var dur = _audio.Duration;
            TimeText = $"{pos:mm\\:ss} / {dur:mm\\:ss}";
            Progress = dur.TotalSeconds > 0 ? (pos.TotalSeconds / dur.TotalSeconds) * 1000 : 0;
            IsPlaying = _audio.IsPlaying;

            // Отсчёт ответа
            if (AnswerActive && _gc.Engine != null)
            {
                AnswerSecondsLeft = (int)Math.Ceiling(_gc.Engine.TimeLeftForAnswer.TotalSeconds);
                if (AnswerSecondsLeft < 0) AnswerSecondsLeft = 0;
            }
        }

        private string GetDisplayName(string mac)
        {
            if (string.IsNullOrEmpty(mac)) return "";
            var snap = _gc.Buttons?.GetByMac(mac);
            return snap?.Label ?? mac;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string p = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}