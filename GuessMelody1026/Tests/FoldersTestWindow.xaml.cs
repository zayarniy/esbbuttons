using GuessMelody.Core.Audio;
using GuessMelody.Core.Game;
using GuessMelody.Core.Models;
using GuessMelody.Core.Storage;
using GuessMelody.Wpf;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Threading;

namespace GuessMelody.Tests
{
    public partial class FoldersTestWindow : Window
    {
        private readonly NaAudioEngine _audio = new NaAudioEngine();
        private FolderConfig _config;
        private FolderManager _manager;
        private DispatcherTimer _uiTimer;

        private string _selectedTrackName;
        private CategoryConfig _selectedCategory;

        private const string FolderConfigPath = "folders.json";

        public FoldersTestWindow()
        {
            InitializeComponent();

            // Пробуем загрузить конфиг, если есть
            _config = JsonStore.Load<FolderConfig>(FolderConfigPath);
            _manager = new FolderManager(_config);

            // Если корневая папка валидна — пересканируем и покажем
            if (!string.IsNullOrWhiteSpace(_config.RootPath) &&
                Directory.Exists(_config.RootPath))
            {
                try
                {
                    _manager.Scan();
                    Log($"Автозагрузка: {_config.RootPath}");
                }
                catch (Exception ex) { Log("Ошибка автоскана: " + ex.Message); }
            }

            DefaultStartBox.Text = _config.DefaultPreviewStartSec.ToString("F1");
            //RebuildTree();
            Dispatcher.BeginInvoke(new Action(RebuildTree));

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _uiTimer.Tick += (_, __) => UpdatePlaybackUI();
            _uiTimer.Start();

            _audio.TrackEnded += (_, __) =>
                Dispatcher.BeginInvoke(new Action(() => Log("Трек закончился")));

            Closed += (_, __) =>
            {
                _uiTimer.Stop();
                _audio.Dispose();
            };
        }

        // =============================================================
        // Верхняя панель
        // =============================================================
        private void PickFolder_Click(object sender, RoutedEventArgs e)
        {
            // FolderBrowserDialog — простой и надёжный вариант для .NET Framework
            // Если проект на .NET 8+, можно использовать OpenFolderDialog.
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Выберите папку игры (внутри — подпапки-категории)"
            };

            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            try
            {
                _manager.SetRootAndScan(dlg.SelectedPath);
                Log($"Корневая папка: {dlg.SelectedPath}");
                Log($"Категорий: {_manager.TotalCategories}, треков: {_manager.TotalTracks}");
                RootPathText.Text = dlg.SelectedPath;
                //RebuildTree();
                Dispatcher.BeginInvoke(new Action(RebuildTree));
                AutoSave();
            }
            catch (Exception ex) { Log("Ошибка выбора папки: " + ex.Message); }
        }

        private void Rescan_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _manager.Scan();
                Log($"Пересканировано. Категорий: {_manager.TotalCategories}, треков: {_manager.TotalTracks}");
                //RebuildTree();
                Dispatcher.BeginInvoke(new Action(RebuildTree));
                AutoSave();
            }
            catch (Exception ex) { Log("Ошибка скана: " + ex.Message); }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                JsonStore.Save(FolderConfigPath, _config);
                Log($"Сохранено → {Path.GetFullPath(FolderConfigPath)}");
            }
            catch (Exception ex) { Log("Ошибка сохранения: " + ex.Message); }
        }

        private void Load_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _config = JsonStore.Load<FolderConfig>(FolderConfigPath);
                _manager = new FolderManager(_config);
                DefaultStartBox.Text = _config.DefaultPreviewStartSec.ToString("F1");
                RootPathText.Text = _config.RootPath;
                //RebuildTree();
                Dispatcher.BeginInvoke(new Action(RebuildTree));
                Log($"Загружено. Категорий: {_manager.TotalCategories}, треков: {_manager.TotalTracks}");
            }
            catch (Exception ex) { Log("Ошибка загрузки: " + ex.Message); }
        }

        private void DefaultStart_LostFocus(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(DefaultStartBox.Text, out var sec) && sec >= 0)
            {
                _config.DefaultPreviewStartSec = sec;
                Log($"Default preview start = {sec:F1} c");
                UpdateEffectiveStartText();
                AutoSave();
            }
            else
            {
                DefaultStartBox.Text = _config.DefaultPreviewStartSec.ToString("F1");
            }
        }

        // =============================================================
        // Дерево
        // =============================================================
        private void RebuildTree()
        {
            if (Tree==null) return;  // На старте конструктора ещё не создано
            var mgr = AppServices.FolderManager;
            if (mgr == null || mgr.Config == null) return;
            Tree.Items.Clear();

            foreach (var cat in _manager.Categories)
            {
                var catNode = new TreeViewItem
                {
                    Header = $"{cat.Name}  ({cat.TrackCount})",
                    Tag = cat,
                    IsExpanded = false
                };

                foreach (var track in cat.Tracks)
                {
                    var trackNode = new TreeViewItem
                    {
                        Header = track,
                        Tag = new TrackRef(cat, track)
                    };
                    catNode.Items.Add(trackNode);
                }

                Tree.Items.Add(catNode);
            }

            RootPathText.Text = _config.RootPath ?? "(не задана)";
        }

        private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var item = e.NewValue as TreeViewItem;
            if (item?.Tag is TrackRef tr)
            {
                _selectedCategory = tr.Category;
                _selectedTrackName = tr.TrackName;

                TrackStartBox.IsEnabled = true;
                ApplyTrackBtn.IsEnabled = true;
                ResetTrackBtn.IsEnabled = true;
                TrackStartBox.Text = _manager.GetPreviewStartSec(tr.Category, tr.TrackName)
                                             .ToString("F1");

                UpdateEffectiveStartText();
                InfoText.Text = $"Категория: {tr.Category.Name}\n" +
                                $"Трек:      {tr.TrackName}\n" +
                                $"Полный:    {_manager.GetFullTrackPath(tr.Category, tr.TrackName)}";
            }
            else if (item?.Tag is CategoryConfig cat)
            {
                _selectedCategory = cat;
                _selectedTrackName = null;

                TrackStartBox.IsEnabled = false;
                ApplyTrackBtn.IsEnabled = false;
                ResetTrackBtn.IsEnabled = false;

                InfoText.Text = $"Категория: {cat.Name}\n" +
                                $"Путь:      {cat.FullPath}\n" +
                                $"Треков:    {cat.TrackCount}";
            }
        }

        private void Tree_MouseDoubleClick(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_selectedTrackName == null) return;
            PlaySelected();
        }

        // =============================================================
        // Плеер
        // =============================================================
        private void PlaySelected()
        {
            if (_selectedTrackName == null || _selectedCategory == null) return;

            var path = _manager.GetFullTrackPath(_selectedCategory, _selectedTrackName);
            if (!File.Exists(path)) { Log($"Файл не найден: {path}"); return; }

            try
            {
                _audio.Load(path);

                double startSec = _manager.GetPreviewStartSec(_selectedCategory,
                                                              _selectedTrackName);
                if (startSec > 0)
                    _audio.Seek(TimeSpan.FromSeconds(startSec));

                _audio.Play();
                Log($"▶ {_selectedCategory.Name} / {_selectedTrackName}  (с {startSec:F1} c)");
            }
            catch (Exception ex) { Log("Ошибка воспроизведения: " + ex.Message); }
        }

        private void Play_Click(object sender, RoutedEventArgs e) => PlaySelected();
        private void Pause_Click(object sender, RoutedEventArgs e) => _audio.Pause();
        private void Stop_Click(object sender, RoutedEventArgs e) => _audio.Stop();

        private void VolSlider_ValueChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (_audio == null) return;
            _audio.Volume = (float)VolSlider.Value;
            if (VolText != null) VolText.Text = $"{_audio.Volume:P0}";
        }

        private void UpdatePlaybackUI()
        {
            var pos = _audio.Position;
            var dur = _audio.Duration;
            TimeText.Text = $"{pos:mm\\:ss\\.fff} / {dur:mm\\:ss\\.fff}";

            if (dur > TimeSpan.Zero)
                Progress.Value = (pos.TotalSeconds / dur.TotalSeconds) * 1000.0;
        }

        // =============================================================
        // Старт трека
        // =============================================================
        private void ApplyTrackStart_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCategory == null || _selectedTrackName == null) return;
            if (!double.TryParse(TrackStartBox.Text, out var sec) || sec < 0)
            {
                Log("Некорректное значение старта.");
                return;
            }
            _manager.SetPreviewStartSec(_selectedCategory, _selectedTrackName, sec);
            UpdateEffectiveStartText();
            Log($"Для {_selectedTrackName} старт = {sec:F1} c");
            AutoSave();
        }

        private void ResetTrackStart_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCategory == null || _selectedTrackName == null) return;
            _manager.ClearPreviewStartSec(_selectedCategory, _selectedTrackName);
            TrackStartBox.Text = _config.DefaultPreviewStartSec.ToString("F1");
            UpdateEffectiveStartText();
            Log($"Сброшен персональный старт для {_selectedTrackName}");
            AutoSave();
        }

        private void TrackStart_LostFocus(object sender, RoutedEventArgs e)
        {
            // Не применяем автоматически — только по кнопке,
            // чтобы пользователь мог спокойно отредактировать.
        }

        private void UpdateEffectiveStartText()
        {
            if (_selectedCategory == null || _selectedTrackName == null)
            {
                EffectiveStartText.Text = "";
                return;
            }
            var sec = _manager.GetPreviewStartSec(_selectedCategory, _selectedTrackName);
            var hasPersonal = _config.PreviewStartSec.ContainsKey(
                _config.Key(_selectedCategory, _selectedTrackName));
            EffectiveStartText.Text = hasPersonal
                ? $"Действует персональное значение: {sec:F1} c"
                : $"Действует значение по умолчанию: {sec:F1} c";
        }

        // =============================================================
        // Прочее
        // =============================================================
        private void AutoSave()
        {
            try { JsonStore.Save(FolderConfigPath, _config); }
            catch (Exception ex) { Log("AutoSave: " + ex.Message); }
        }

        private void Log(string s)
        {
            LogList.Items.Add($"[{DateTime.Now:HH:mm:ss.fff}] {s}");
            if (LogList.Items.Count > 500) LogList.Items.RemoveAt(0);
            LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
        }

        private class TrackRef
        {
            public CategoryConfig Category { get; }
            public string TrackName { get; }
            public TrackRef(CategoryConfig cat, string track) { Category = cat; TrackName = track; }
        }
    }
}