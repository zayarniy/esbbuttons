using GuessMelody.Audio;
using GuessMelody.Core;
using GuessMelody.Core.Enums;
using GuessMelody.Core.Models;
using GuessMelody.Core.Serialization;
using GuessMelody.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;

namespace GuessMelody.ViewModels
{
    public class FoldersTabViewModel : ViewModelBase, IDisposable
    {
        private readonly LogService _log;
        private readonly DialogService _dlg;
        private readonly FolderScanner _scanner;
        private readonly PreviewEngine _preview;
        private readonly AudioCoordinator _audioCoord;

        public ObservableCollection<CategoryRowViewModel> Categories { get; }
            = new ObservableCollection<CategoryRowViewModel>();

        public ObservableCollection<TrackRowViewModel> Tracks { get; }
            = new ObservableCollection<TrackRowViewModel>();

        private readonly DispatcherTimer _previewTimer;

        public FoldersTabViewModel(
            LogService log,
            DialogService dlg,
            FolderScanner scanner,
            PreviewEngine preview,
            AudioCoordinator audioCoord)
        {
            _log = log;
            _dlg = dlg;
            _scanner = scanner;
            _preview = preview;
            _audioCoord = audioCoord;

            BrowseFolderCommand = new RelayCommand(_ => BrowseFolder());
            RefreshCommand = new RelayCommand(_ => Refresh());
            LoadCommand = new RelayCommand(_ => LoadJson());
            SaveCommand = new RelayCommand(_ => SaveJson());
            SaveAsCommand = new RelayCommand(_ => SaveJsonAs());
            PreviewCommand = new RelayCommand(p => PreviewTrack(p as TrackRowViewModel));
            StopPreviewCommand = new RelayCommand(_ => StopPreview());
            ResetTrackCommand = new RelayCommand(_ => ResetSelectedTrack());
            RandomTrackCommand = new RelayCommand(_ => SelectRandomTrack());
            ApplyDefaultsToAllCommand = new RelayCommand(_ => ApplyDefaultsToAll());

            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _previewTimer.Tick += (_, __) =>
            {
                if (_preview.IsPlaying)
                {
                    PreviewPositionSec = _preview.CurrentPositionSec;
                    PreviewDurationSec = _preview.TotalDurationSec;
                }
                else
                {
                    PreviewPositionSec = 0;
                    _previewTimer.Stop();
                    foreach (var t in Tracks) t.IsPlaying = false;
                    OnPropertyChanged(nameof(IsPreviewPlaying));
                }
            };
        }

        // ---------------------------------------------------------
        // Команды
        // ---------------------------------------------------------
        public ICommand BrowseFolderCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand LoadCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand SaveAsCommand { get; }
        public ICommand PreviewCommand { get; }
        public ICommand StopPreviewCommand { get; }
        public ICommand ResetTrackCommand { get; }
        public ICommand RandomTrackCommand { get; }
        public ICommand ApplyDefaultsToAllCommand { get; }

        // ---------------------------------------------------------
        // Свойства: корневая папка и пресет
        // ---------------------------------------------------------
        private string _rootFolder = "";
        public string RootFolder
        {
            get => _rootFolder;
            set { if (Set(ref _rootFolder, value)) _audioCoord.RootFolder = value; }
        }

        private string _presetName = "folders";
        public string PresetName
        {
            get => _presetName;
            set => Set(ref _presetName, value);
        }

        // ---------------------------------------------------------
        // Выбор
        // ---------------------------------------------------------
        private CategoryRowViewModel _selectedCategory;
        public CategoryRowViewModel SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (Set(ref _selectedCategory, value))
                    LoadTracksForCategory(value);
            }
        }

        private TrackRowViewModel _selectedTrack;
        public TrackRowViewModel SelectedTrack
        {
            get => _selectedTrack;
            set
            {
                if (Set(ref _selectedTrack, value))
                    LoadTrackSettings(value);
            }
        }

        // ---------------------------------------------------------
        // Per-track настройки выбранного трека
        // ---------------------------------------------------------
        private bool _useDefaults = true;
        public bool UseDefaults
        {
            get => _useDefaults;
            set
            {
                if (Set(ref _useDefaults, value))
                {
                    OnPropertyChanged(nameof(OverridesEnabled));
                    if (!value && SelectedTrack?.Track.Overrides == null)
                    {
                        // создаём override на базе defaults
                        SelectedTrack.Track.Overrides = new TrackOverrides
                        {
                            StartSec = Defaults.StartSec,
                            DurationSec = Defaults.DurationSec,
                            Volume = Defaults.Volume,
                            Loop = Defaults.Loop,
                            RandomStart = Defaults.RandomStart
                        };
                        LoadTrackSettings(SelectedTrack);
                    }
                    if (value && SelectedTrack != null)
                        SelectedTrack.Track.Overrides = null;
                    SelectedTrack?.RefreshOverrides();
                }
            }
        }

        public bool OverridesEnabled => !UseDefaults && SelectedTrack != null;

        // Настройки выбранного трека
        private double _startSec;
        public double StartSec
        {
            get => _startSec;
            set { if (Set(ref _startSec, value)) SaveCurrentTrackOverride(); }
        }

        private double _durationSec;
        public double DurationSec
        {
            get => _durationSec;
            set { if (Set(ref _durationSec, value)) SaveCurrentTrackOverride(); }
        }

        private int _volume;
        public int Volume
        {
            get => _volume;
            set { if (Set(ref _volume, value)) SaveCurrentTrackOverride(); }
        }

        private bool _loop;
        public bool Loop
        {
            get => _loop;
            set { if (Set(ref _loop, value)) SaveCurrentTrackOverride(); }
        }

        private bool _randomStart;
        public bool RandomStart
        {
            get => _randomStart;
            set { if (Set(ref _randomStart, value)) SaveCurrentTrackOverride(); }
        }

        // ---------------------------------------------------------
        // Defaults (по умолчанию для всех)
        // ---------------------------------------------------------
        private TrackOverrides _defaults = new TrackOverrides
        {
            StartSec = 0,
            DurationSec = 20,
            Volume = 80,
            Loop = false,
            RandomStart = false
        };
        public TrackOverrides Defaults
        {
            get => _defaults;
            set => Set(ref _defaults, value);
        }

        // ---------------------------------------------------------
        // Preview
        // ---------------------------------------------------------
        private double _previewPositionSec;
        public double PreviewPositionSec
        {
            get => _previewPositionSec;
            set => Set(ref _previewPositionSec, value);
        }

        private double _previewDurationSec;
        public double PreviewDurationSec
        {
            get => _previewDurationSec;
            set => Set(ref _previewDurationSec, value);
        }

        public bool IsPreviewPlaying => _preview.IsPlaying;

        // ---------------------------------------------------------
        // Логика
        // ---------------------------------------------------------
        private void BrowseFolder()
        {
            var path = _dlg.BrowseFolder("Выберите папку с игрой");
            if (string.IsNullOrEmpty(path)) return;
            RootFolder = path;
            Refresh();
        }

        private void Refresh()
        {
            if (string.IsNullOrEmpty(RootFolder) || !Directory.Exists(RootFolder))
            {
                _dlg.Warn("Сначала выберите существующую папку.");
                return;
            }

            var cats = _scanner.Scan(RootFolder, computeDuration: true);

            Categories.Clear();
            Tracks.Clear();

            foreach (var c in cats)
            {
                // сохраняем overrides из текущей модели, если папка уже была раньше
                MergeOverridesFromExisting(c);
                Categories.Add(new CategoryRowViewModel(c));
            }

            SelectedCategory = Categories.FirstOrDefault();

            _audioCoord.RootFolder = RootFolder;
        }

        private void MergeOverridesFromExisting(Category fresh)
        {
            var old = _defaultsAppliedCategories?.FirstOrDefault(c =>
                string.Equals(c.Name, fresh.Name, StringComparison.OrdinalIgnoreCase));
            if (old == null) return;

            foreach (var t in fresh.Tracks)
            {
                var oldT = old.Tracks.FirstOrDefault(x =>
                    string.Equals(x.RelativePath, t.RelativePath, StringComparison.OrdinalIgnoreCase));
                if (oldT?.Overrides != null)
                    t.Overrides = oldT.Overrides.Clone();
            }
        }

        private List<Category> _defaultsAppliedCategories;
        public void ApplyFromSettings(GameSettings s)
        {
            if (s?.Folders == null) return;
            _defaultsAppliedCategories = s.Folders.Categories?.ToList();
            RootFolder = s.Folders.RootFolder ?? "";
            PresetName = s.Folders.PresetName ?? "folders";
            Defaults = s.Folders.Defaults ?? new TrackOverrides();

            Categories.Clear();
            Tracks.Clear();

            if (_defaultsAppliedCategories != null)
            {
                foreach (var c in _defaultsAppliedCategories)
                    Categories.Add(new CategoryRowViewModel(c));
                SelectedCategory = Categories.FirstOrDefault();
            }
            _audioCoord.RootFolder = RootFolder;
        }

        public void ApplyToSettings(GameSettings s)
        {
            if (s == null) return;
            if (s.Folders == null) s.Folders = new FoldersCatalog();

            s.Folders.RootFolder = RootFolder;
            s.Folders.PresetName = PresetName;
            s.Folders.Defaults = Defaults;
            s.Folders.Categories = Categories.Select(c => c.Category).ToList();

            _defaultsAppliedCategories = s.Folders.Categories.ToList();
        }

        private void LoadTracksForCategory(CategoryRowViewModel row)
        {
            Tracks.Clear();
            if (row?.Category?.Tracks == null) return;

            foreach (var t in row.Category.Tracks)
                Tracks.Add(new TrackRowViewModel(t));

            SelectedTrack = Tracks.FirstOrDefault();
        }

        private void LoadTrackSettings(TrackRowViewModel row)
        {
            if (row == null) return;

            bool hasOverrides = row.Track.Overrides != null;
            UseDefaults = !hasOverrides;

            if (hasOverrides)
            {
                StartSec = row.Track.Overrides.StartSec ?? (Defaults.StartSec ?? 0);
                DurationSec = row.Track.Overrides.DurationSec ?? (Defaults.DurationSec ?? 20);
                Volume = row.Track.Overrides.Volume ?? (Defaults.Volume ?? 80);
                Loop = row.Track.Overrides.Loop ?? (Defaults.Loop ?? false);
                RandomStart = row.Track.Overrides.RandomStart ?? (Defaults.RandomStart ?? false);
            }
            else
            {
                StartSec = Defaults.StartSec ?? 0;
                DurationSec = Defaults.DurationSec ?? 20;
                Volume = Defaults.Volume ?? 80;
                Loop = Defaults.Loop ?? false;
                RandomStart = Defaults.RandomStart ?? false;
            }

            OnPropertyChanged(nameof(OverridesEnabled));
        }

        private void SaveCurrentTrackOverride()
        {
            if (SelectedTrack == null || UseDefaults) return;

            SelectedTrack.Track.Overrides = new TrackOverrides
            {
                StartSec = StartSec,
                DurationSec = DurationSec,
                Volume = Volume,
                Loop = Loop,
                RandomStart = RandomStart
            };

            SelectedTrack.RefreshOverrides();
        }

        private void ResetSelectedTrack()
        {
            if (SelectedTrack == null) return;
            SelectedTrack.Track.Overrides = null;
            UseDefaults = true;
            LoadTrackSettings(SelectedTrack);
            SelectedTrack.RefreshOverrides();
            _log.Add(LogKind.System, $"Override сброшен: {SelectedTrack.FileName}");
        }

        private void ApplyDefaultsToAll()
        {
            if (Categories.Count == 0) return;
            if (!_dlg.Confirm("Снять все per-track override у всех треков?")) return;

            int count = 0;
            foreach (var c in Categories)
                foreach (var t in c.Category.Tracks)
                    if (t.Overrides != null) { t.Overrides = null; count++; }

            foreach (var tr in Tracks) tr.RefreshOverrides();
            _log.Add(LogKind.System, $"Сброшено {count} overrides.");
        }

        private void SelectRandomTrack()
        {
            if (Tracks.Count == 0) return;
            var rnd = new Random();
            SelectedTrack = Tracks[rnd.Next(Tracks.Count)];
        }

        private void PreviewTrack(TrackRowViewModel row)
        {
            if (row == null) return;

            StopPreview();

            var full = Path.IsPathRooted(row.Track.RelativePath)
                ? row.Track.RelativePath
                : Path.Combine(RootFolder ?? "", row.Track.RelativePath);

            if (!File.Exists(full))
            {
                _dlg.Warn("Файл не найден:\n" + full);
                return;
            }

            double startSec = row.Track.Overrides?.StartSec ?? Defaults.StartSec ?? 0;
            double durSec = row.Track.Overrides?.DurationSec ?? Defaults.DurationSec ?? 20;
            int vol = row.Track.Overrides?.Volume ?? Defaults.Volume ?? 80;
            bool loop = row.Track.Overrides?.Loop ?? Defaults.Loop ?? false;

            _preview.Play(full, startSec, durSec, vol, loop);
            row.IsPlaying = true;
            OnPropertyChanged(nameof(IsPreviewPlaying));
            _previewTimer.Start();

            _log.Add(LogKind.Audio, $"Preview: {row.FileName} [{startSec:F1}+{durSec}s]");
        }

        private void StopPreview()
        {
            _preview.Stop();
            foreach (var t in Tracks) t.IsPlaying = false;
            PreviewPositionSec = 0;
            OnPropertyChanged(nameof(IsPreviewPlaying));
            _previewTimer.Stop();
        }

        // ---------------------------------------------------------
        // Сохранение / загрузка
        // ---------------------------------------------------------
        private void SaveJson()
        {
            var name = string.IsNullOrWhiteSpace(PresetName) ? "folders" : PresetName;
            var path = _dlg.SaveFile("JSON (*.json)|*.json", name + ".json");
            if (string.IsNullOrEmpty(path)) return;

            var data = new FoldersCatalog
            {
                RootFolder = RootFolder,
                PresetName = PresetName,
                Defaults = Defaults,
                Categories = Categories.Select(c => c.Category).ToList()
            };

            if (JsonStore.Save(path, data))
                _log.Add(LogKind.System, $"folders.json сохранён: {path}");
            else
                _dlg.Error("Не удалось сохранить файл.");
        }

        private void SaveJsonAs()
        {
            // просто вызывает SaveJson с диалогом
            SaveJson();
        }

        private void LoadJson()
        {
            var path = _dlg.OpenFile("JSON (*.json)|*.json");
            if (string.IsNullOrEmpty(path)) return;

            var data = JsonStore.Load<FoldersCatalog>(path);
            if (data == null)
            {
                _dlg.Error("Не удалось прочитать файл.");
                return;
            }

            RootFolder = data.RootFolder ?? "";
            PresetName = data.PresetName ?? "folders";
            Defaults = data.Defaults ?? new TrackOverrides();
            _defaultsAppliedCategories = data.Categories?.ToList();

            Categories.Clear();
            Tracks.Clear();
            if (data.Categories != null)
                foreach (var c in data.Categories)
                    Categories.Add(new CategoryRowViewModel(c));

            SelectedCategory = Categories.FirstOrDefault();
            _audioCoord.RootFolder = RootFolder;
            _log.Add(LogKind.System, $"folders.json загружен: {path}");
        }

        public void Dispose()
        {
            _previewTimer?.Stop();
            _preview?.Stop();
        }
    }
}