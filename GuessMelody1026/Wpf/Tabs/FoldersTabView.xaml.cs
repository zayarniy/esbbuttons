using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GuessMelody.Core.Game;
using GuessMelody.Core.Models;
using GuessMelody.Wpf.Logging;

namespace GuessMelody.Wpf.Tabs
{
    public partial class FoldersTabView : UserControl
    {
        private FolderManager Manager => AppServices.FolderManager;
        private FolderConfig Config => AppServices.FolderConfig;

        private DispatcherTimer _uiTimer;

        private string _selectedTrackName;
        private CategoryConfig _selectedCategory;

        public FoldersTabView()
        {
            InitializeComponent();

            DefaultStartBox.Text = (Config?.DefaultPreviewStartSec ?? 0).ToString("F1");
            RootPathText.Text = Config?.RootPath ?? "(не задана)";

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _uiTimer.Tick += (_, __) => UpdatePlaybackUI();

            Loaded += (_, __) => { _uiTimer.Start();
                //RebuildTree();
                Dispatcher.BeginInvoke(new Action(RebuildTree));
                RootPathText.Text = Config?.RootPath ?? "(не задана)"; };
            Unloaded += (_, __) => _uiTimer.Stop();
        }

        // =============================================================
        // Верхняя панель
        // =============================================================
        private void PickFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Выберите папку игры (внутри — подпапки-категории)"
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            try
            {
                Manager.SetRootAndScan(dlg.SelectedPath);
                Log($"Корневая папка: {dlg.SelectedPath}");
                Log($"Категорий: {Manager.TotalCategories}, треков: {Manager.TotalTracks}");
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
                Manager.Scan();
                Log($"Пересканировано. Категорий: {Manager.TotalCategories}, " +
                    $"треков: {Manager.TotalTracks}");
                //RebuildTree();
                Dispatcher.BeginInvoke(new Action(RebuildTree));
                AutoSave();
            }
            catch (Exception ex) { Log("Ошибка скана: " + ex.Message); }
        }

        // =============================================================
        // Сохранить в текущий файл
        // =============================================================
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppServices.SaveFolderConfig();
                UpdateConfigPathText();
                Log($"Сохранено → {Path.GetFullPath(AppServices.FolderConfigPath)}");
            }
            catch (Exception ex) { Log("Ошибка сохранения: " + ex.Message); }
        }

        // =============================================================
        // Показ текущего пути файла настроек
        // =============================================================
        private void UpdateConfigPathText()
        {
            try
            {
                ConfigPathText.Text = Path.GetFullPath(AppServices.FolderConfigPath);
            }
            catch
            {
                ConfigPathText.Text = AppServices.FolderConfigPath;
            }
        }

        // =============================================================
        // Загрузить из...
        // =============================================================
        private void LoadFrom_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Загрузить настройки папки",
                Filter = "JSON (*.json)|*.json|Все файлы (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                var loaded = GuessMelody.Core.Storage.JsonStore
                    .Load<FolderConfig>(dlg.FileName);

                // Копируем поля в существующий объект — чтобы не рвать ссылки
                Config.RootPath = loaded.RootPath;
                Config.DefaultPreviewStartSec = loaded.DefaultPreviewStartSec;
                Config.Categories = loaded.Categories;
                Config.PreviewStartSec = loaded.PreviewStartSec;

                // Пересканируем — на случай, если папка изменилась на диске
                Manager.Scan();

                // Делаем этот файл активным — автосохранение пойдёт в него
                AppServices.SetFolderConfigPath(dlg.FileName);

                // Обновляем UI
                DefaultStartBox.Text = Config.DefaultPreviewStartSec.ToString("F1");
                RootPathText.Text = Config.RootPath;
                UpdateConfigPathText();
                //RebuildTree();
                Dispatcher.BeginInvoke(new Action(RebuildTree));

                Log($"Загружено из {dlg.FileName}. " +
                    $"Категорий: {Manager.TotalCategories}, треков: {Manager.TotalTracks}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка загрузки",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Log("Ошибка загрузки: " + ex.Message);
            }
        }

        // =============================================================
        // Сохранить как...
        // =============================================================
        private void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Сохранить настройки папки",
                Filter = "JSON (*.json)|*.json|Все файлы (*.*)|*.*",
                FileName = Path.GetFileName(AppServices.FolderConfigPath),
                DefaultExt = ".json",
                AddExtension = true,
                OverwritePrompt = true
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                // Сохраняем во ВЫБРАННЫЙ файл
                GuessMelody.Core.Storage.JsonStore.Save(dlg.FileName, Config);

                // Делаем этот файл активным — дальше автосохранение идёт туда
                AppServices.SetFolderConfigPath(dlg.FileName);

                UpdateConfigPathText();
                Log($"Сохранили как → {dlg.FileName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка сохранения",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Log("Ошибка сохранения как: " + ex.Message);
            }
        }

        private void Load_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Перезагружаем конфиг из файла и пересобираем менеджер
                var loaded = GuessMelody.Core.Storage.JsonStore
                    .Load<FolderConfig>(AppServices.FolderConfigPath);

                // Копируем в существующий объект — иначе ссылки порвутся
                Config.RootPath = loaded.RootPath;
                Config.DefaultPreviewStartSec = loaded.DefaultPreviewStartSec;
                Config.Categories = loaded.Categories;
                Config.PreviewStartSec = loaded.PreviewStartSec;

                // FolderManager работает с тем же объектом Config — просто пересканируем
                Manager.Scan();

                DefaultStartBox.Text = Config.DefaultPreviewStartSec.ToString("F1");
                RootPathText.Text = Config.RootPath;
                //RebuildTree();
                Dispatcher.BeginInvoke(new Action(RebuildTree));
                Log($"Загружено. Категорий: {Manager.TotalCategories}, треков: {Manager.TotalTracks}");
            }
            catch (Exception ex) { Log("Ошибка загрузки: " + ex.Message); }
        }

        private void DefaultStart_LostFocus(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(DefaultStartBox.Text, out var sec) && sec >= 0)
            {
                Config.DefaultPreviewStartSec = sec;
                Log($"Старт по умолчанию = {sec:F1} c");
                UpdateEffectiveStartText();
                AutoSave();
            }
            else
            {
                DefaultStartBox.Text = Config.DefaultPreviewStartSec.ToString("F1");
            }
        }

        // =============================================================
        // Дерево
        // =============================================================
        private void RebuildTree()
        {
            
            if (Tree==null) return;
            Tree.Items.Clear();
            if (Manager==null || Manager.Categories==null) return;  
            foreach (var cat in Manager.Categories)
            {
                var catNode = new TreeViewItem
                {
                    Header = $"{cat.Name}  ({cat.TrackCount})",
                    Tag = cat,
                    IsExpanded = false
                };
                if (cat.Tracks == null) return;
                foreach (var track in cat.Tracks)
                {
                    catNode.Items.Add(new TreeViewItem
                    {
                        Header = track,
                        Tag = new TrackRef(cat, track)
                    });
                }

                Tree.Items.Add(catNode);
            }
        }

        private void Tree_SelectedItemChanged(object sender,
            RoutedPropertyChangedEventArgs<object> e)
        {
            var item = e.NewValue as TreeViewItem;
            if (item?.Tag is TrackRef tr)
            {
                _selectedCategory = tr.Category;
                _selectedTrackName = tr.TrackName;

                TrackStartBox.IsEnabled = true;
                ApplyTrackBtn.IsEnabled = true;
                ResetTrackBtn.IsEnabled = true;
                TrackStartBox.Text =
                    Manager.GetPreviewStartSec(tr.Category, tr.TrackName).ToString("F1");

                UpdateEffectiveStartText();
                InfoText.Text = $"Категория: {tr.Category.Name}\n" +
                                $"Трек:      {tr.TrackName}\n" +
                                $"Полный:    {Manager.GetFullTrackPath(tr.Category, tr.TrackName)}";
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
            if (_selectedTrackName != null) PlaySelected();
        }

        // =============================================================
        // Плеер
        // =============================================================
        private void PlaySelected()
        {
            if (_selectedTrackName == null || _selectedCategory == null) return;

            var path = Manager.GetFullTrackPath(_selectedCategory, _selectedTrackName);
            if (!File.Exists(path)) { Log($"Файл не найден: {path}"); return; }

            try
            {
                AppServices.Audio.Load(path);
                double startSec = Manager.GetPreviewStartSec(_selectedCategory,
                                                              _selectedTrackName);
                if (startSec > 0) AppServices.Audio.Seek(TimeSpan.FromSeconds(startSec));
                AppServices.Audio.Play();
                Log($"▶ {_selectedCategory.Name} / {_selectedTrackName}  (с {startSec:F1} c)");
            }
            catch (Exception ex) { Log("Ошибка воспроизведения: " + ex.Message); }
        }

        private void Play_Click(object sender, RoutedEventArgs e) => PlaySelected();
        private void Pause_Click(object sender, RoutedEventArgs e) => AppServices.Audio.Pause();
        private void Stop_Click(object sender, RoutedEventArgs e) => AppServices.Audio.Stop();

        private void VolSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (AppServices.Audio == null) return;
            AppServices.Audio.Volume = (float)VolSlider.Value;
            if (VolText != null) VolText.Text = $"{AppServices.Audio.Volume:P0}";
        }

        private void UpdatePlaybackUI()
        {
            var a = AppServices.Audio;
            if (a == null) return;

            var pos = a.Position;
            var dur = a.Duration;
            TimeText.Text = $"{pos:mm\\:ss\\.fff} / {dur:mm\\:ss\\.fff}";
            if (dur > TimeSpan.Zero)
                Progress.Value = (pos.TotalSeconds / dur.TotalSeconds) * 1000.0;
        }

        // =============================================================
        // Старт превью
        // =============================================================
        private void ApplyTrackStart_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCategory == null || _selectedTrackName == null) return;
            if (!double.TryParse(TrackStartBox.Text, out var sec) || sec < 0)
            { Log("Некорректное значение старта."); return; }

            Manager.SetPreviewStartSec(_selectedCategory, _selectedTrackName, sec);
            UpdateEffectiveStartText();
            Log($"Для {_selectedTrackName} старт = {sec:F1} c");
            AutoSave();
        }

        private void ResetTrackStart_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCategory == null || _selectedTrackName == null) return;
            Manager.ClearPreviewStartSec(_selectedCategory, _selectedTrackName);
            TrackStartBox.Text = Config.DefaultPreviewStartSec.ToString("F1");
            UpdateEffectiveStartText();
            Log($"Сброшен персональный старт для {_selectedTrackName}");
            AutoSave();
        }

        private void UpdateEffectiveStartText()
        {
            if (_selectedCategory == null || _selectedTrackName == null)
            { EffectiveStartText.Text = ""; return; }

            var sec = Manager.GetPreviewStartSec(_selectedCategory, _selectedTrackName);
            var hasPersonal = Config.PreviewStartSec.ContainsKey(
                Config.Key(_selectedCategory, _selectedTrackName));
            EffectiveStartText.Text = hasPersonal
                ? $"Действует персональное: {sec:F1} c"
                : $"Действует по умолчанию: {sec:F1} c";
        }

        // =============================================================
        private void AutoSave()
        {
            try { AppServices.SaveFolderConfig(); }
            catch (Exception ex) { Log("AutoSave: " + ex.Message); }
        }

        private void Log(string s)
        {
            LogList.Items.Add($"[{DateTime.Now:HH:mm:ss.fff}] {s}");
            if (LogList.Items.Count > 500) LogList.Items.RemoveAt(0);
            LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
            AppLogger.Instance.Info("[Folders] " + s);
        }

        private class TrackRef
        {
            public CategoryConfig Category { get; }
            public string TrackName { get; }
            public TrackRef(CategoryConfig cat, string track)
            { Category = cat; TrackName = track; }
        }

        private void TrackStartBox_LostFocus(object sender, RoutedEventArgs e)
        {

        }
    }
}