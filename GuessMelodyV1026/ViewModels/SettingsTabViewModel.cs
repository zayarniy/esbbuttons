using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using GuessMelody.Audio;
using GuessMelody.Core;
using GuessMelody.Core.Enums;
using GuessMelody.Core.Models;
using GuessMelody.Services;

namespace GuessMelody.ViewModels
{
    public class SettingsTabViewModel : ViewModelBase
    {
        private readonly GameEngine _engine;
        private readonly AudioEngine _audio;
        private readonly LogService _log;
        private readonly DialogService _dlg;
        private readonly ColorPickerService _color;
        private readonly SettingsPresetService _presets;

        public SettingsTabViewModel(
            GameEngine engine,
            AudioEngine audio,
            LogService log,
            DialogService dlg,
            ColorPickerService color,
            SettingsPresetService presets)
        {
            _engine = engine;
            _audio = audio;
            _log = log;
            _dlg = dlg;
            _color = color;
            _presets = presets;

            PickBuzzerCommand = new RelayCommand(_ => PickSoundFile(nameof(BuzzerPath), v => BuzzerPath = v));
            PickCorrectCommand = new RelayCommand(_ => PickSoundFile(nameof(CorrectPath), v => CorrectPath = v));
            PickWrongCommand = new RelayCommand(_ => PickSoundFile(nameof(WrongPath), v => WrongPath = v));
            PickTickCommand = new RelayCommand(_ => PickSoundFile(nameof(TickPath), v => TickPath = v));
            TestBuzzerCommand = new RelayCommand(_ => TestSound(BuzzerPath));
            TestCorrectCommand = new RelayCommand(_ => TestSound(CorrectPath));
            TestWrongCommand = new RelayCommand(_ => TestSound(WrongPath));
            TestTickCommand = new RelayCommand(_ => TestSound(TickPath));

            PickBgColorCommand = new RelayCommand(_ =>
            {
                var v = _color.PickHex(PlayerBgColor);
                if (v != null) PlayerBgColor = v;
            });
            PickAccentColorCommand = new RelayCommand(_ =>
            {
                var v = _color.PickHex(PlayerAccentColor);
                if (v != null) PlayerAccentColor = v;
            });
            PickTextColorCommand = new RelayCommand(_ =>
            {
                var v = _color.PickHex(PlayerTextColor);
                if (v != null) PlayerTextColor = v;
            });

            ApplyCommand = new RelayCommand(_ => ApplyToSettings(_engine.Settings, pushToEngine: true));
            ResetDefaultsCommand = new RelayCommand(_ => ResetToDefaults());
            SavePresetCommand = new RelayCommand(_ => SavePreset());
            LoadPresetCommand = new RelayCommand(_ => LoadPreset());
            DeletePresetCommand = new RelayCommand(_ => DeletePreset());
            RefreshPresetsCommand = new RelayCommand(_ => RefreshPresetList());

            RefreshPresetList();

            // стартовое заполнение из настроек
            ApplyFromSettings(_engine.Settings);
        }

        // ---------------------------------------------------------
        // Команды
        // ---------------------------------------------------------
        public ICommand PickBuzzerCommand { get; }
        public ICommand PickCorrectCommand { get; }
        public ICommand PickWrongCommand { get; }
        public ICommand PickTickCommand { get; }
        public ICommand TestBuzzerCommand { get; }
        public ICommand TestCorrectCommand { get; }
        public ICommand TestWrongCommand { get; }
        public ICommand TestTickCommand { get; }
        public ICommand PickBgColorCommand { get; }
        public ICommand PickAccentColorCommand { get; }
        public ICommand PickTextColorCommand { get; }
        public ICommand ApplyCommand { get; }
        public ICommand ResetDefaultsCommand { get; }
        public ICommand SavePresetCommand { get; }
        public ICommand LoadPresetCommand { get; }
        public ICommand DeletePresetCommand { get; }
        public ICommand RefreshPresetsCommand { get; }

        // ---------------------------------------------------------
        // Раздел «Игра»
        // ---------------------------------------------------------
        private int _playerCount = 3;
        public int PlayerCount { get => _playerCount; set => Set(ref _playerCount, value); }

        private int _roundCount = 5;
        public int RoundCount { get => _roundCount; set => Set(ref _roundCount, value); }

        private int _fragmentSec = 20;
        public int FragmentSec { get => _fragmentSec; set => Set(ref _fragmentSec, value); }

        private int _defaultStart = 0;
        public int DefaultStartSec { get => _defaultStart; set => Set(ref _defaultStart, value); }

        private bool _randomStart;
        public bool RandomStart { get => _randomStart; set => Set(ref _randomStart, value); }

        private bool _countdown = true;
        public bool CountdownOnAnswer { get => _countdown; set => Set(ref _countdown, value); }

        private bool _setupMode;
        public bool IsSetupMode { get => _setupMode; set => Set(ref _setupMode, value); }

        private bool _allowRePress;
        public bool AllowRePress { get => _allowRePress; set => Set(ref _allowRePress, value); }

        private int _scorePer = 1;
        public int ScorePerCorrect { get => _scorePer; set => Set(ref _scorePer, value); }

        // ---------------------------------------------------------
        // Раздел «Звуки»
        // ---------------------------------------------------------
        private int _volume = 80;
        public int Volume
        {
            get => _volume;
            set { if (Set(ref _volume, value)) _audio?.SetVolume(value); }
        }

        private string _buzzerPath = "";
        public string BuzzerPath { get => _buzzerPath; set => Set(ref _buzzerPath, value); }

        private string _correctPath = "";
        public string CorrectPath { get => _correctPath; set => Set(ref _correctPath, value); }

        private string _wrongPath = "";
        public string WrongPath { get => _wrongPath; set => Set(ref _wrongPath, value); }

        private string _tickPath = "";
        public string TickPath { get => _tickPath; set => Set(ref _tickPath, value); }

        private int _fadeIn = 100;
        public int FadeInMs { get => _fadeIn; set => Set(ref _fadeIn, value); }

        private int _fadeOut = 100;
        public int FadeOutMs { get => _fadeOut; set => Set(ref _fadeOut, value); }

        private bool _tick5 = true;
        public bool TickLast5 { get => _tick5; set => Set(ref _tick5, value); }

        // ---------------------------------------------------------
        // Раздел «Тайминги»
        // ---------------------------------------------------------
        private int _cdBefore = 3;
        public int CountdownBeforeSec { get => _cdBefore; set => Set(ref _cdBefore, value); }

        private int _answerTimeout = 30;
        public int AnswerTimeoutSec { get => _answerTimeout; set => Set(ref _answerTimeout, value); }

        private int _pausePress = 0;
        public int PauseAfterPressMs { get => _pausePress; set => Set(ref _pausePress, value); }

        private int _pauseRounds = 3;
        public int PauseBetweenRoundsSec { get => _pauseRounds; set => Set(ref _pauseRounds, value); }

        private bool _autoNext;
        public bool AutoNextRound { get => _autoNext; set => Set(ref _autoNext, value); }


        // ---------------------------------------------------------
        // Раздел «Внешний вид»
        // ---------------------------------------------------------
        private int _hbTimeout = 11;
        public int HeartbeatTimeoutSec
        {
            get => _hbTimeout;
            set => Set(ref _hbTimeout, value);
        }

        private string _bg = "#1E1E2E";
        public string PlayerBgColor
        {
            get => _bg;
            set
            {
                if (Set(ref _bg, value))
                {
                    OnPropertyChanged(nameof(BgPreviewBrush));
                    PushAppearanceToEngine();
                }
            }
        }

        private string _accent = "#FFD166";
        public string PlayerAccentColor
        {
            get => _accent;
            set
            {
                if (Set(ref _accent, value))
                {
                    OnPropertyChanged(nameof(AccentPreviewBrush));
                    PushAppearanceToEngine();
                }
            }
        }

        private string _text = "#EEEEEE";
        public string PlayerTextColor
        {
            get => _text;
            set
            {
                if (Set(ref _text, value))
                {
                    OnPropertyChanged(nameof(TextPreviewBrush));
                    PushAppearanceToEngine();
                }
            }
        }

        private int _catFont = 32;
        public int CategoryFontSize
        {
            get => _catFont;
            set { if (Set(ref _catFont, value)) PushAppearanceToEngine(); }
        }

        private int _nameFont = 28;
        public int PlayerNameFontSize
        {
            get => _nameFont;
            set { if (Set(ref _nameFont, value)) PushAppearanceToEngine(); }
        }

        private int _scoreFont = 40;
        public int PlayerScoreFontSize
        {
            get => _scoreFont;
            set { if (Set(ref _scoreFont, value)) PushAppearanceToEngine(); }
        }

        private string _theme = "Light";
        public string AppTheme { get => _theme; set => Set(ref _theme, value); }

        private int _logFont = 12;
        public int LogFontSize { get => _logFont; set => Set(ref _logFont, value); }

        public System.Windows.Media.Brush BgPreviewBrush => ParseBrush(PlayerBgColor);
        public System.Windows.Media.Brush AccentPreviewBrush => ParseBrush(PlayerAccentColor);
        public System.Windows.Media.Brush TextPreviewBrush => ParseBrush(PlayerTextColor);

        private static System.Windows.Media.Brush ParseBrush(string hex)
        {
            try
            {
                return (System.Windows.Media.Brush)
                    new System.Windows.Media.BrushConverter().ConvertFromString(hex);
            }
            catch { return System.Windows.Media.Brushes.Black; }
        }

        // ---------------------------------------------------------
        // Раздел «Пресеты»
        // ---------------------------------------------------------
        public ObservableCollection<string> PresetNames { get; } = new ObservableCollection<string>();

        private string _selectedPreset;
        public string SelectedPreset
        {
            get => _selectedPreset;
            set => Set(ref _selectedPreset, value);
        }

        private string _presetName = "default";
        public string PresetName { get => _presetName; set => Set(ref _presetName, value); }

        // ---------------------------------------------------------
        // Работа с моделью
        // ---------------------------------------------------------
        public void ApplyFromSettings(GameSettings s)
        {
            if (s == null) return;

            var r = s.Rules ?? new GameRules();
            var a = s.Audio ?? new AudioSettings();
            var t = s.Timings ?? new TimingSettings();
            var ap = s.Appearance ?? new AppearanceSettings();

            
            PlayerCount = r.PlayerCount;
            RoundCount = r.RoundCount;
            FragmentSec = r.FragmentSec;
            DefaultStartSec = r.DefaultStartSec;
            RandomStart = r.RandomStart;
            CountdownOnAnswer = r.CountdownOnAnswer;
            IsSetupMode = r.IsSetupMode;
            AllowRePress = r.AllowRePressAfterWrong;
            ScorePerCorrect = r.ScorePerCorrect;

            Volume = a.Volume;
            BuzzerPath = a.BuzzerPath;
            CorrectPath = a.CorrectPath;
            WrongPath = a.WrongPath;
            TickPath = a.TickPath;
            FadeInMs = a.FadeInMs;
            FadeOutMs = a.FadeOutMs;
            TickLast5 = a.TickLastFiveSeconds;

            CountdownBeforeSec = t.CountdownBeforeSec;
            AnswerTimeoutSec = t.AnswerTimeoutSec;
            PauseAfterPressMs = t.PauseAfterPressMs;
            PauseBetweenRoundsSec = t.PauseBetweenRoundsSec;
            AutoNextRound = t.AutoNextRound;
            HeartbeatTimeoutSec = t.HeartbeatTimeoutSec;

            PlayerBgColor = ap.PlayerBgColor;
            PlayerAccentColor = ap.PlayerAccentColor;
            PlayerTextColor = ap.PlayerTextColor;
            CategoryFontSize = ap.CategoryFontSize;
            PlayerNameFontSize = ap.PlayerNameFontSize;
            PlayerScoreFontSize = ap.PlayerScoreFontSize;
            AppTheme = ap.AppTheme;
            LogFontSize = ap.LogFontSize;
        }

        /// <summary>Переносит состояние VM в GameSettings. Если pushToEngine == true — дополнительно обновляет Settings у движка и поднимает события.</summary>
        public void ApplyToSettings(GameSettings s, bool pushToEngine = false)
        {
            if (s == null) return;
            if (s.Rules == null) s.Rules = new GameRules();
            if (s.Audio == null) s.Audio = new AudioSettings();
            if (s.Timings == null) s.Timings = new TimingSettings();
            if (s.Appearance == null) s.Appearance = new AppearanceSettings();

            var r = s.Rules; var a = s.Audio; var t = s.Timings; var ap = s.Appearance;

            r.PlayerCount = PlayerCount;
            r.RoundCount = RoundCount;
            r.FragmentSec = FragmentSec;
            r.DefaultStartSec = DefaultStartSec;
            r.RandomStart = RandomStart;
            r.CountdownOnAnswer = CountdownOnAnswer;
            r.IsSetupMode = IsSetupMode;
            r.AllowRePressAfterWrong = AllowRePress;
            r.ScorePerCorrect = ScorePerCorrect;

            a.Volume = Volume;
            a.BuzzerPath = BuzzerPath;
            a.CorrectPath = CorrectPath;
            a.WrongPath = WrongPath;
            a.TickPath = TickPath;
            a.FadeInMs = FadeInMs;
            a.FadeOutMs = FadeOutMs;
            a.TickLastFiveSeconds = TickLast5;

            t.CountdownBeforeSec = CountdownBeforeSec;
            t.AnswerTimeoutSec = AnswerTimeoutSec;
            t.PauseAfterPressMs = PauseAfterPressMs;
            t.PauseBetweenRoundsSec = PauseBetweenRoundsSec;
            t.AutoNextRound = AutoNextRound;
            t.HeartbeatTimeoutSec = HeartbeatTimeoutSec;

            ap.PlayerBgColor = PlayerBgColor;
            ap.PlayerAccentColor = PlayerAccentColor;
            ap.PlayerTextColor = PlayerTextColor;
            ap.CategoryFontSize = CategoryFontSize;
            ap.PlayerNameFontSize = PlayerNameFontSize;
            ap.PlayerScoreFontSize = PlayerScoreFontSize;
            ap.AppTheme = AppTheme;
            ap.LogFontSize = LogFontSize;

            if (pushToEngine && _engine != null)
            {
                _log.Add(LogKind.System, "Настройки применены к движку");
                // GameEngine.ApplySettings поднимет событие StateChanged; окна подхватят
                _engine.ApplySettings(s);
            }
        }

        private void ResetToDefaults()
        {
            if (!_dlg.Confirm("Сбросить все настройки к значениям по умолчанию?")) return;

            ApplyFromSettings(new GameSettings());
            ApplyToSettings(_engine.Settings, pushToEngine: true);
            _log.Add(LogKind.System, "Настройки сброшены к умолчанию.");
        }

        private void PushAppearanceToEngine()
        {
            if (_engine?.Settings?.Appearance == null) return;
            var ap = _engine.Settings.Appearance;
            ap.PlayerBgColor = PlayerBgColor;
            ap.PlayerAccentColor = PlayerAccentColor;
            ap.PlayerTextColor = PlayerTextColor;
            ap.CategoryFontSize = CategoryFontSize;
            ap.PlayerNameFontSize = PlayerNameFontSize;
            ap.PlayerScoreFontSize = PlayerScoreFontSize;
            _engine.NotifySettingsChanged();
        }

        // ---------------------------------------------------------
        // Файлы звуков
        // ---------------------------------------------------------
        private void PickSoundFile(string fieldName, Action<string> setter)
        {
            var path = _dlg.OpenFile("Аудио (*.wav;*.mp3)|*.wav;*.mp3|Все файлы (*.*)|*.*",
                "Выберите звуковой файл");
            if (string.IsNullOrEmpty(path)) return;
            setter(path);
            _log.Add(LogKind.System, $"{fieldName} = {path}");
        }

        private void TestSound(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            {
                _dlg.Warn("Файл не выбран или не существует.");
                return;
            }
            // Проигрываем через BuzzerPlayer — он не мешает музыке
            var buzzer = new Audio.BuzzerPlayer();
            buzzer.Play(path, Volume);
            // Отложенно освободим — не критично
            System.Threading.Tasks.Task.Delay(5000).ContinueWith(_ => buzzer.Dispose());
        }

        // ---------------------------------------------------------
        // Пресеты
        // ---------------------------------------------------------
        public void RefreshPresetList()
        {
            PresetNames.Clear();
            foreach (var n in _presets.ListNames())
                PresetNames.Add(n);
            if (PresetNames.Count > 0 && string.IsNullOrEmpty(SelectedPreset))
                SelectedPreset = PresetNames[0];
        }

        private void SavePreset()
        {
            var name = PresetName?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                _dlg.Warn("Введите имя пресета.");
                return;
            }
            var preset = new SettingsPreset
            {
                Name = name,
                Rules = new GameRules(),
                Audio = new AudioSettings(),
                Timings = new TimingSettings(),
                Appearance = new AppearanceSettings()
            };
            // заполняем из текущих настроек
            var tmp = new GameSettings
            {
                Rules = preset.Rules,
                Audio = preset.Audio,
                Timings = preset.Timings,
                Appearance = preset.Appearance
            };
            ApplyToSettings(tmp);

            if (_presets.Save(name, preset))
            {
                _log.Add(LogKind.System, $"Пресет сохранён: {name}");
                RefreshPresetList();
                SelectedPreset = name;
            }
            else
            {
                _dlg.Error("Не удалось сохранить пресет.");
            }
        }

        private void LoadPreset()
        {
            if (string.IsNullOrEmpty(SelectedPreset)) return;

            var preset = _presets.Load(SelectedPreset);
            if (preset == null)
            {
                _dlg.Error("Не удалось загрузить пресет.");
                return;
            }

            var tmp = new GameSettings
            {
                Rules = preset.Rules ?? new GameRules(),
                Audio = preset.Audio ?? new AudioSettings(),
                Timings = preset.Timings ?? new TimingSettings(),
                Appearance = preset.Appearance ?? new AppearanceSettings()
            };
            ApplyFromSettings(tmp);
            ApplyToSettings(_engine.Settings, pushToEngine: true);
            _log.Add(LogKind.System, $"Пресет применён: {SelectedPreset}");
        }

        private void DeletePreset()
        {
            if (string.IsNullOrEmpty(SelectedPreset)) return;
            if (!_dlg.Confirm($"Удалить пресет «{SelectedPreset}»?")) return;
            if (_presets.Delete(SelectedPreset))
            {
                _log.Add(LogKind.System, $"Пресет удалён: {SelectedPreset}");
                RefreshPresetList();
            }
        }
    }
}