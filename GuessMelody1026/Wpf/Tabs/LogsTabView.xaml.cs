using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GuessMelody.Wpf.Game;
using GuessMelody.Wpf.Logging;
using Microsoft.Win32;

namespace GuessMelody.Wpf.Tabs
{
    public partial class LogsTabView : UserControl
    {
        private readonly AppLogger _logger = AppLogger.Instance;
        private readonly ObservableCollection<RoundRecordRow> _roundRows =
            new ObservableCollection<RoundRecordRow>();

        public LogsTabView()
        {
            InitializeComponent();

            // Привязываем фильтры к свойствам AppLogger
            var bindingSrc = _logger;
            DataContext = bindingSrc;

            RoundsGrid.ItemsSource = _roundRows;

            Loaded += (_, __) =>
            {
                // Подписки
                GameController.Instance.RoundFinished += OnRoundFinished;
                _logger.EntryAdded += OnEntryAdded;

                // Начальная отрисовка истории
                RefreshRounds();
                RefreshStats();
                UpdateCounts();
            };

            Unloaded += (_, __) =>
            {
                GameController.Instance.RoundFinished -= OnRoundFinished;
                _logger.EntryAdded -= OnEntryAdded;
            };
        }

        // =============================================================
        // Журнал
        // =============================================================
        private void OnEntryAdded(object s, LogEntry e) =>
            Dispatcher.BeginInvoke(new Action(UpdateCounts));

        private void UpdateCounts()
        {
            CountText.Text = $"журнал: {_logger.Entries.Count}, раундов: {_roundRows.Count}";
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            _logger.Clear();
            UpdateCounts();
        }

        private void ExportLogCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = $"log_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                _logger.ExportCsv(dlg.FileName);
                _logger.Info($"Журнал экспортирован: {dlg.FileName} ({_logger.Entries.Count} записей)");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка экспорта",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportLogTxt_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "TXT (*.txt)|*.txt",
                FileName = $"log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                _logger.ExportTxt(dlg.FileName);
                _logger.Info($"Журнал экспортирован (TXT): {dlg.FileName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка экспорта",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =============================================================
        // История раундов
        // =============================================================
        private void OnRoundFinished(object s, Core.Models.RoundRecord r)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                RefreshRounds();
                RefreshStats();
                UpdateCounts();
            }));
        }

        private void RefreshRounds()
        {
            _roundRows.Clear();
            foreach (var r in GameController.Instance.History)
            {
                _roundRows.Add(new RoundRecordRow
                {
                    RoundNumber = r.RoundNumber,
                    Category = r.Category,
                    TrackFile = System.IO.Path.GetFileName(r.TrackFile ?? ""),
                    WinnerName = r.WinnerName ?? r.WinnerMac ?? "—",
                    ScoreText = r.Score == 0 ? "0" : (r.Score > 0 ? $"+{r.Score}" : r.Score.ToString()),
                    Reason = TranslateReason(r.Reason),
                    AnswerTimeSec = r.AnswerTimeSec,
                    FinishedLocal = r.FinishedLocal
                });
            }
        }

        private static string TranslateReason(Core.Models.EndReason r)
        {
            switch (r)
            {
                case Core.Models.EndReason.Scored: return "✅ Угадано";
                case Core.Models.EndReason.NoOne: return "⏱ Никто не нажал";
                case Core.Models.EndReason.AllAnsweredWrong: return "❌ Все не угадали";
                case Core.Models.EndReason.NoTracks: return "🚫 Треки кончились";
                case Core.Models.EndReason.Manual: return "🚫 Закрыт вручную";
                default: return r.ToString();
            }
        }

        private void ExportRoundsCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = $"rounds_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                GameController.Instance.ExportRoundsCsv(dlg.FileName);
                _logger.Info($"История раундов экспортирована: {dlg.FileName} " +
                             $"({GameController.Instance.History.Count} раундов)");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка экспорта",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearRounds_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Очистить историю раундов? Очки игроков сохранятся.",
                    "Подтверждение", MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            // Историю хранит GameController. Добавим метод ClearHistory().
            GameController.Instance.ClearHistory();
            RefreshRounds();
            RefreshStats();
            UpdateCounts();
        }

        // =============================================================
        // Статистика
        // =============================================================
        private void RefreshStats()
        {
            var st = GameController.Instance.GetStats();

            StatsText.Text =
                $"Всего раундов:      {st.TotalRounds}\n" +
                $"Угадано (балл > 0):  {st.ScoredRounds}\n" +
                $"Никто не нажал:      {st.NoOneRounds}\n" +
                $"Все не угадали:      {st.AllWrongRounds}\n" +
                $"Среднее время ответа: {st.AverageAnswerSec:F1} с";

            WinsGrid.ItemsSource = st.WinsByPlayer
                .OrderByDescending(kv => kv.Value)
                .Select(kv => new { Key = kv.Key, Value = kv.Value })
                .ToList();
        }
    }

    // =============================================================
    // Строка истории раундов для DataGrid
    // =============================================================
    public class RoundRecordRow
    {
        public int RoundNumber { get; set; }
        public string Category { get; set; }
        public string TrackFile { get; set; }
        public string WinnerName { get; set; }
        public string ScoreText { get; set; }
        public string Reason { get; set; }
        public double AnswerTimeSec { get; set; }
        public string FinishedLocal { get; set; }
    }
}