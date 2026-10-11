using GuessMelody.Core.Game;
using GuessMelody.Core.Models;
using GuessMelody.Core.Storage;
using GuessMelody.Wpf.Game;
using GuessMelody.Wpf.Logging;
using GuessMelody.Wpf.ViewModels;
using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GuessMelody.Wpf.Views
{
    public partial class HostConsoleWindow : Window
    {
        public HostConsoleViewModel ViewModel { get; }

        // --- Превью трека ---
        private string _previewCategoryName;
        private string _previewTrackName;

        // Запомним последний выбранный пресет
        private string _currentPresetPath;



        public HostConsoleWindow(HostConsoleViewModel vm)
        {
            InitializeComponent();
            ViewModel = vm;
            DataContext = vm;

            Loaded += (_, __) =>
            {
                BuildPreviewTree();
                PresetPathText.Text = _currentPresetPath ?? "(не задан)";
                UpdatePresetPathText();
            };
            // Хоткеи для эмуляции кнопок: F1..F6
           // PreviewKeyDown += HostConsoleWindow_PreviewKeyDown;
        }
        //private void HostConsoleWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        //{
        //    if (e.Key < Key.F1 || e.Key > Key.F6) return;

        //    int slot = e.Key - Key.F1;   // 0..5
        //    var mac = ResolveSlotMac(slot);
        //    if (string.IsNullOrEmpty(mac)) return;

        //    EmulatePress(mac);
        //    e.Handled = true;
        //}

        /// <summary>
        /// Возвращает MAC для слота 0..5.
        /// Сначала пробует по PlayerSlot, потом по индексу в списке кнопок,
        /// если кнопок нет — использует фейковый MAC вида EMU:01.
        /// </summary>
        private string ResolveSlotMac(int slot0Based)
        {
            AppLogger.Instance.Info($"[Пульт] Эмуляция кнопки слота {slot0Based + 1}");
            var svc = AppServices.ButtonService;
            if (svc == null)
            {
                // Сервер кнопок не запущен — эмулируем «фантомную» кнопку
                return $"EMU:{slot0Based + 1:D2}";
            }

            var all = svc.GetAll();

            // 1) По PlayerSlot = slot0Based + 1
            int targetSlot = slot0Based + 1;
            var bySlot = all.FirstOrDefault(b => b.PlayerSlot == targetSlot);
            if (bySlot != null) return bySlot.Mac;

            // 2) Иначе — по индексу, среди подключённых
            if (slot0Based < all.Count)
                return all[slot0Based].Mac;

            // 3) Фейковая кнопка (для теста без железа)
            return $"EMU:{targetSlot:D2}";
        }

        private void EmulatePress(string mac)
        {
            
            var svc = AppServices.ButtonService;
            if (svc != null)
            {
                svc.EmulatePress(mac);
            }
            else
            {
                // Если сервер не запущен — хотя бы запишем в лог,
                // чтобы было видно, что хоткей сработал.
                AppLogger.Instance.Press($"[EMU-F] {mac} (сервер кнопок не запущен)");
            }
        }
        // =============================================================
        // ПРЕСЕТЫ
        // =============================================================
        private void SavePreset_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Сохранить пресет (настройки игры + папки + кнопки)",
                Filter = "Пресет (*.json)|*.json|Все файлы (*.*)|*.*",
                FileName = $"preset_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                DefaultExt = ".json",
                AddExtension = true
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                PresetService.Save(
                    dlg.FileName,
                    AppServices.GameSettings,
                    AppServices.ButtonBindings,
                    AppServices.FolderConfig);

                _currentPresetPath = dlg.FileName;
                PresetPathText.Text = dlg.FileName;
                UpdatePresetPathText();
                AppLogger.Instance.Info($"Пресет сохранён: {dlg.FileName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка сохранения пресета",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadPreset_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Загрузить пресет",
                Filter = "Пресет (*.json)|*.json|Все файлы (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() != true) return;

            var preset = PresetService.Load(dlg.FileName);
            if (preset == null)
            {
                MessageBox.Show("Не удалось прочитать пресет.",
                    "Ошибка загрузки", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Если идёт раунд — предупредить
            var gc = GameController.Instance;
            if (gc.Engine != null && gc.Engine.State != RoundState.Idle)
            {
                if (MessageBox.Show(
                        "Сейчас идёт раунд. Загрузить пресет? Раунд будет закрыт.",
                        "Подтверждение", MessageBoxButton.YesNo,
                        MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;

                gc.HostSaysNoOne();
            }

            // 1) Применяем пресет — копируя поля/элементы
            PresetService.ApplyTo(
                preset,
                AppServices.GameSettings,
                AppServices.ButtonBindings,
                AppServices.FolderConfig);
            
            // 2) Пересчитываем FullPath у категорий (без скана диска)
            try { AppServices.FolderManager.RefreshPaths(); } catch { }
            UpdatePresetPathText();
            // 3) Обновляем привязки кнопок, если сервис активен
            if (AppServices.ButtonService != null)
            {
                AppServices.ButtonService.ApplyBindings(AppServices.ButtonBindings.Bindings);
            }

            // 4) Перестраиваем дерево превью
            BuildPreviewTree();

            // 5) Обновляем путь пресета и надпись
            _currentPresetPath = dlg.FileName;
            PresetPathText.Text = dlg.FileName;

            AppLogger.Instance.Info(
                $"Пресет загружен: {dlg.FileName} " +
                $"(«{preset.Name}», сохранён {preset.SavedAtUtc.ToLocalTime():g})");

            // 6) Обновляем VM пульта
            ViewModel.RefreshFromEngine();

            // 7) Проверка: если папка недоступна — предупредить
            if (!Directory.Exists(AppServices.FolderConfig.RootPath))
            {
                MessageBox.Show(
                    $"Папка игры из пресета не найдена:\n" +
                    $"{AppServices.FolderConfig.RootPath}\n\n" +
                    $"Дерево пустое или показывает старые треки. " +
                    $"Укажите правильную папку на вкладке «Папки».",
                    "Папка не найдена", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void UpdatePresetPathText()
        {
            if (!string.IsNullOrEmpty(AppServices.CurrentPresetPath))
            {
                PresetPathText.Text = AppServices.CurrentPresetPath;
                _currentPresetPath = AppServices.CurrentPresetPath;
            }
            else
            {
                // Пресет не загружался — покажем, что используется текущий folders.json
                PresetPathText.Text =
                    "(активен " + System.IO.Path.GetFullPath(
                        AppServices.FolderConfigPath) + ")";
            }
        }

        // =============================================================
        // ДЕРЕВО ПРЕВЬЮ
        // =============================================================
        private void BuildPreviewTree()
        {
            PreviewTree.Items.Clear();

            var fm = AppServices.FolderManager;
            if (fm == null) return;

            foreach (var cat in fm.Categories)
            {
                var catNode = new TreeViewItem
                {
                    Header = $"{cat.Name}  ({cat.TrackCount})",
                    Tag = cat,
                    IsExpanded = false
                };

                foreach (var track in cat.Tracks)
                {
                    catNode.Items.Add(new TreeViewItem
                    {
                        Header = track,
                        Tag = new TrackRef(cat, track)
                    });
                }

                PreviewTree.Items.Add(catNode);
            }
        }

        private void PreviewTree_SelectedItemChanged(object sender,
            RoutedPropertyChangedEventArgs<object> e)
        {
            var item = e.NewValue as TreeViewItem;
            if (item?.Tag is TrackRef tr)
            {
                _previewCategoryName = tr.Category.Name;
                _previewTrackName = tr.TrackName;
                SelectedTrackText.Text = $"{tr.Category.Name} / {tr.TrackName}";
            }
            else
            {
                _previewCategoryName = null;
                _previewTrackName = null;
                SelectedTrackText.Text = "";
            }
        }

        private void PreviewTree_MouseDoubleClick(object sender,
            MouseButtonEventArgs e)
        {
            PreviewTrack_Click(sender, e);
        }

        // =============================================================
        // ПРОСЛУШИВАНИЕ ТРЕКА
        // =============================================================
        private void PreviewTrack_Click(object sender, RoutedEventArgs e)
        {
            if (_previewTrackName == null)
            {
                MessageBox.Show("Выберите трек в дереве.",
                    "Прослушать", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var fm = AppServices.FolderManager;
            var cat = fm.Categories.FirstOrDefault(c =>
                string.Equals(c.Name, _previewCategoryName, StringComparison.OrdinalIgnoreCase));
            if (cat == null) return;

            var path = fm.GetFullTrackPath(cat, _previewTrackName);
            if (!File.Exists(path))
            {
                MessageBox.Show($"Файл не найден:\n{path}",
                    "Прослушать", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                AppServices.Audio.Load(path);

                // Стартуем с сохранённой «персональной» секунды, как на вкладке Папки
                double startSec = fm.GetPreviewStartSec(cat, _previewTrackName);
                if (startSec > 0)
                    AppServices.Audio.Seek(TimeSpan.FromSeconds(startSec));

                AppServices.Audio.Play();
                AppLogger.Instance.Info(
                    $"[Пульт] Превью: {_previewCategoryName} / {_previewTrackName} " +
                    $"(с {startSec:F1} с)");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка воспроизведения",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PreviewStop_Click(object sender, RoutedEventArgs e)
        {
            try { AppServices.Audio.Stop(); }
            catch { /* ignore */ }
        }

        // =============================================================
        // ПЛЕЕР (остальные кнопки без изменений)
        // =============================================================
        private void Play_Click(object s, RoutedEventArgs e) => ViewModel.Play();
        private void Pause_Click(object s, RoutedEventArgs e) => ViewModel.Pause();
        private void Stop_Click(object s, RoutedEventArgs e) => ViewModel.Stop();

        private void Back10_Click(object s, RoutedEventArgs e) => ViewModel.SeekRelative(-10);
        private void Fwd10_Click(object s, RoutedEventArgs e) => ViewModel.SeekRelative(+10);

        private void HostYes_Click(object s, RoutedEventArgs e) => ViewModel.HostSaysYes(2);
        private void HostNo_Click(object s, RoutedEventArgs e) => ViewModel.HostSaysNo();
        private void HostNoOne_Click(object s, RoutedEventArgs e) => ViewModel.HostSaysNoOne();

        private void StartCat1_Click(object s, RoutedEventArgs e) => ViewModel.StartRound(0);
        private void StartCat2_Click(object s, RoutedEventArgs e) => ViewModel.StartRound(1);
        private void StartCat3_Click(object s, RoutedEventArgs e) => ViewModel.StartRound(2);
        private void StartCat4_Click(object s, RoutedEventArgs e) => ViewModel.StartRound(3);
        private void StartCat5_Click(object s, RoutedEventArgs e) => ViewModel.StartRound(4);
        private void StartCat6_Click(object s, RoutedEventArgs e) => ViewModel.StartRound(5);
        private void StartCat7_Click(object s, RoutedEventArgs e) => ViewModel.StartRound(6);
        private void StartCat8_Click(object s, RoutedEventArgs e) => ViewModel.StartRound(7);
        private void StartCat9_Click(object s, RoutedEventArgs e) => ViewModel.StartRound(8);

        private void ResetRound_Click(object s, RoutedEventArgs e)
        {
            ViewModel.Stop();
            ViewModel.HostSaysNoOne();
        }

        private void ResetScores_Click(object s, RoutedEventArgs e)
        {
            if (MessageBox.Show("Сбросить очки всех игроков?",
                    "Подтверждение", MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            ViewModel.ResetScores();
        }

        // =============================================================
        // Вспомогательный класс для TreeView
        // =============================================================
        private class TrackRef
        {
            public CategoryConfig Category { get; }
            public string TrackName { get; }
            public TrackRef(CategoryConfig cat, string track)
            { Category = cat; TrackName = track; }
        }
    }
}