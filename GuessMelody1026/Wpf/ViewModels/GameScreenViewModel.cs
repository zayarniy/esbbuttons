using GuessMelody.Core.Game;
using GuessMelody.Core.Models;
using GuessMelody.Wpf.Game;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace GuessMelody.Wpf.ViewModels
{
    public enum ScreenMode
    {
        CategorySelect,   // показываем сетку категорий
        Countdown,        // 3-2-1
        Playing,          // играет трек, лампа серая
        Answer,           // кто-то нажал, идёт отсчёт на ответ
        Result            // «Правильно» / «Неправильно»
    }


    public class CategoryTile : INotifyPropertyChanged
    {
        public int Index { get; set; }          // 1..9
        public string Name { get; set; }
        public int TracksLeft { get; set; }
        public int TracksTotal { get; set; }

        private bool _isHighlighted;
        public bool IsHighlighted
        {
            get => _isHighlighted;
            set { _isHighlighted = value; OnPropertyChanged(); }
        }

        private bool _isEmpty;
        public bool IsEmpty
        {
            get => _isEmpty;
            set { _isEmpty = value; OnPropertyChanged(); }
        }

        public string CounterText => $"{TracksLeft}/{TracksTotal}";

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string p = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    public class ScoreRow : INotifyPropertyChanged
    {
        public string Mac { get; set; }
        public string Name { get; set; }

        private int _score;
        public int Score
        {
            get => _score;
            set { _score = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string p = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    public class GameScreenViewModel : INotifyPropertyChanged
    {

        private DispatcherTimer _answerTimer;

        private readonly RoundEngine _engine;

        public ObservableCollection<CategoryTile> Categories { get; } = new ObservableCollection<CategoryTile>();
        public ObservableCollection<ScoreRow> Scores { get; } = new ObservableCollection<ScoreRow>();

        // ---------- Режим экрана ----------
        private ScreenMode _mode = ScreenMode.CategorySelect;
        public ScreenMode Mode
        {
            get => _mode;
            private set
            {
                if (_mode == value) return;
                _mode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCategorySelect));
                OnPropertyChanged(nameof(IsCountdown));
                OnPropertyChanged(nameof(IsPlaying));
                OnPropertyChanged(nameof(IsAnswer));
                OnPropertyChanged(nameof(IsResult));
            }
        }

        public bool IsCategorySelect => Mode == ScreenMode.CategorySelect;
        public bool IsCountdown => Mode == ScreenMode.Countdown;
        public bool IsPlaying => Mode == ScreenMode.Playing;
        public bool IsAnswer => Mode == ScreenMode.Answer;
        public bool IsResult => Mode == ScreenMode.Result;

        // ---------- Заголовок ----------
        public int RoundNumber => _engine.RoundNumber;

        private string _roundInfoText = "";
        public string RoundInfoText
        {
            get => _roundInfoText;
            set { _roundInfoText = value; OnPropertyChanged(); }
        }

        // ---------- Большая надпись в центре ----------
        private string _centerText = "";
        public string CenterText
        {
            get => _centerText;
            set { _centerText = value; OnPropertyChanged(); }
        }

        private string _subText = "";
        public string SubText
        {
            get => _subText;
            set { _subText = value; OnPropertyChanged(); }
        }

        // ---------- Текущая категория / трек ----------
        private string _categoryName = "";
        public string CategoryName
        {
            get => _categoryName;
            set { _categoryName = value; OnPropertyChanged(); }
        }

        private string _trackName = "";
        public string TrackName
        {
            get => _trackName;
            set { _trackName = value; OnPropertyChanged(); }
        }

        // ---------- Лампа/победитель ----------
        private string _winnerLabel = "";
        public string WinnerLabel
        {
            get => _winnerLabel;
            set { _winnerLabel = value; OnPropertyChanged(); }
        }

        private bool _lampRed;
        public bool LampRed
        {
            get => _lampRed;
            set { _lampRed = value; OnPropertyChanged(); }
        }

        // ---------- Отсчёты ----------
        private int _countdownValue;
        public int CountdownValue
        {
            get => _countdownValue;
            set { _countdownValue = value; OnPropertyChanged(); }
        }

        private int _answerValue;
        public int AnswerValue
        {
            get => _answerValue;
            set { _answerValue = value; OnPropertyChanged(); }
        }

        // ---------- Результат ----------
        private string _resultText = "";
        public string ResultText
        {
            get => _resultText;
            set { _resultText = value; OnPropertyChanged(); }
        }

        private bool _resultOk;
        public bool ResultOk
        {
            get => _resultOk;
            set { _resultOk = value; OnPropertyChanged(); }
        }

        // ---------- Категория, которую пользователь сейчас «взял» ----------
        private int _selectedCategoryIndex = -1;

        public event EventHandler<int> CategoryChosen;  // 0-based индекс
        public event EventHandler RestartRequested;

        public GameScreenViewModel(RoundEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));

            _engine.StateChanged += (_, s) => OnEngineStateChanged(s);
            _engine.CountdownTick += (_, n) => OnCountdownTick(n);
            _engine.PressAccepted += (_, p) => OnPressAccepted(p);
            _engine.RoundFinished += (_, r) => OnRoundFinished(r);
            _engine.Message += (_, m) => { /* можно показывать в SubText */ };
            // Подписка на изменение счёта
            GameController.Instance.ScoreChanged += OnScoreChanged;

            _answerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _answerTimer.Tick += (_, __) =>
            {
                if (Mode == ScreenMode.Answer)
                {
                    var left = (int)Math.Ceiling(_engine.TimeLeftForAnswer.TotalSeconds);
                    if (left < 0) left = 0;
                    AnswerValue = left;
                }
            };
            _answerTimer.Start();
        }

        private void OnScoreChanged(object sender, string mac)
        {
            // На случай, если событие пришло из фонового потока
            if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    new Action(() => OnScoreChanged(sender, mac)));
                return;
            }

            if (mac == "*")
            {
                // Сброс всех — пересобрать табло
                RefreshAllScores();
                return;
            }

            var row = Scores.FirstOrDefault(s =>
                string.Equals(s.Mac, mac, StringComparison.OrdinalIgnoreCase));
            if (row != null)
                row.Score = GameController.Instance.GetScore(mac);
            else
                RefreshAllScores();
        }

        private void RefreshAllScores()
        {
            // У GameController есть снимок очков — берём его
            foreach (var row in Scores)
                row.Score = GameController.Instance.GetScore(row.Mac);
        }

        // =============================================================
        // Наполнение категорий и табло — вызывается снаружи
        // =============================================================
        public void LoadCategories(IEnumerable<CategoryConfig> cats)
        {
            Categories.Clear();
            int i = 1;
            foreach (var c in cats)
            {
                var left = _engine.TracksLeft(c);
                Categories.Add(new CategoryTile
                {
                    Index = i++,
                    Name = c.Name,
                    TracksLeft = left,
                    TracksTotal = c.Tracks.Count,
                    IsEmpty = left == 0
                });
            }
            OnPropertyChanged(nameof(Categories));
        }

        public void UpdateCategories(IEnumerable<CategoryConfig> cats)
        {
            var list = cats.ToList();
            for (int i = 0; i < Categories.Count && i < list.Count; i++)
            {
                var left = _engine.TracksLeft(list[i]);
                Categories[i].TracksLeft = left;
                Categories[i].IsEmpty = left == 0;
                Categories[i].IsHighlighted = false;
            }
            // Пересобрать текстовое представление счётчика
            for (int i = 0; i < Categories.Count; i++)
            {
                Categories[i] = Categories[i]; // заставит PropertyChanged (при желании)
            }
        }

        public void UpdateScores(IEnumerable<ButtonSnapshot> players,
                                 Func<string, int> scoreGetter)
        {
            Scores.Clear();
            foreach (var p in players)
            {
                Scores.Add(new ScoreRow
                {
                    Mac = p.Mac,
                    Name = p.Label,
                    Score = scoreGetter(p.Mac)
                });
            }
        }

        public void UpdateScore(string mac, int newScore)
        {
            var row = Scores.FirstOrDefault(s =>
                string.Equals(s.Mac, mac, StringComparison.OrdinalIgnoreCase));
            if (row != null) row.Score = newScore;
        }

        // =============================================================
        // Реакция на события RoundEngine
        // =============================================================
        private void OnEngineStateChanged(RoundState s)
        {
            switch (s)
            {
                case RoundState.Countdown:
                    LampRed = false;
                    WinnerLabel = "";
                    CategoryName = _engine.Category?.Name ?? "";
                    TrackName = "";
                    Mode = ScreenMode.Countdown;
                    break;

                case RoundState.TrackPlaying:
                    if (Mode == ScreenMode.Answer)
                    {
                        // Ведущий сказал «Нет» → возвращаемся к проигрыванию
                        LampRed = false;
                        WinnerLabel = "";
                        SubText = "Продолжаем слушать…";
                        Mode = ScreenMode.Playing;
                    }
                    else
                    {
                        Mode = ScreenMode.Playing;
                        CategoryName = _engine.Category?.Name ?? "";
                        TrackName = "";
                        SubText = "Слушайте мелодию";
                    }
                    break;

                case RoundState.WaitingAnswer:
                    LampRed = true;
                    Mode = ScreenMode.Answer;
                    SubText = "Ведущий решает";
                    break;

                case RoundState.ScoreApplied:
                    Mode = ScreenMode.Result;
                    break;

                case RoundState.Idle:
                    // Показываем сетку категорий снова
                    LampRed = false;
                    WinnerLabel = "";
                    ResultText = "";
                    Mode = ScreenMode.CategorySelect;
                    OnPropertyChanged(nameof(RoundNumber));
                    RoundInfoText = _engine.RoundNumber > 0
                        ? $"Раунд {_engine.RoundNumber}"
                        : "";
                    break;
            }
        }

        private void OnCountdownTick(int n)
        {
            CountdownValue = n;
        }

        private void OnPressAccepted(PressEvent p)
        {
            var name = GetPlayerName(p.Mac);
            WinnerLabel = name;
            CenterText = name;
        }

        private void OnRoundFinished(RoundRecord r)
        {
            // ResultText и ResultOk выставит оркестратор (кто знает оценку)
            // Здесь только общая часть.
            if (r.Reason == EndReason.Scored && r.WinnerMac != null)
            {
                ResultText = "✅ Правильно!";
                ResultOk = true;
            }
            else if (r.Reason == EndReason.NoOne ||
                     r.Reason == EndReason.AllAnsweredWrong ||
                     r.Reason == EndReason.Manual)
            {
                ResultText = "❌ Никто не угадал";
                ResultOk = false;
            }

            Mode = ScreenMode.Result;
        }

        // =============================================================
        // Выбор категории (клик по плитке или клавиша 1-9)
        // =============================================================
        public void SelectCategory(int index0based)
        {
            if (!IsCategorySelect) return;
            if (index0based < 0 || index0based >= Categories.Count) return;
            var tile = Categories[index0based];
            if (tile.IsEmpty) return;

            _selectedCategoryIndex = index0based;
            foreach (var c in Categories) c.IsHighlighted = false;
            tile.IsHighlighted = true;

            CategoryName = tile.Name;
            CategoryChosen?.Invoke(this, index0based);
        }

        // =============================================================
        // Для UI: запрос отсчёта на ответ
        // =============================================================
        public void SetAnswerCountdown(int secondsLeft)
        {
            AnswerValue = secondsLeft;
        }

        private string GetPlayerName(string mac)
        {
            var row = Scores.FirstOrDefault(s =>
                string.Equals(s.Mac, mac, StringComparison.OrdinalIgnoreCase));
            return row?.Name ?? mac;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string p = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}