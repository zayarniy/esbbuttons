using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace GuessMelody.Wpf.Logging
{
    public enum LogLevel
    {
        Raw,       // сырой UDP-трафик
        Info,      // общие сообщения
        State,     // смена состояния раунда
        Press,     // нажатия кнопок
        Score,     // изменение счёта
        Error      // ошибки
    }

    public class LogEntry
    {
        public DateTime Time { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; }

        public string TimeText => Time.ToString("HH:mm:ss.fff");
        public string LevelText => Level.ToString();
    }

    /// <summary>
    /// Единый журнал приложения. Один экземпляр на процесс.
    /// Все окна читают одну коллекцию, фильтр — через ICollectionView.
    /// </summary>
    public sealed class AppLogger
    {
        private static readonly Lazy<AppLogger> _instance =
            new Lazy<AppLogger>(() => new AppLogger());
        public static AppLogger Instance => _instance.Value;

        public ObservableCollection<LogEntry> Entries { get; }
            = new ObservableCollection<LogEntry>();

        public ICollectionView View { get; }

        public int LogLimit { get; set; } = 20000;

        // --- Фильтры ---
        public bool ShowRaw { get => _showRaw; set { _showRaw = value; Refresh(); OnFilterChanged(); } }
        public bool ShowInfo { get => _showInfo; set { _showInfo = value; Refresh(); OnFilterChanged(); } }
        public bool ShowState { get => _showState; set { _showState = value; Refresh(); OnFilterChanged(); } }
        public bool ShowPress { get => _showPress; set { _showPress = value; Refresh(); OnFilterChanged(); } }
        public bool ShowScore { get => _showScore; set { _showScore = value; Refresh(); OnFilterChanged(); } }
        public bool ShowError { get => _showError; set { _showError = value; Refresh(); OnFilterChanged(); } }

        private bool _showRaw = false;
        private bool _showInfo = true;
        private bool _showState = true;
        private bool _showPress = true;
        private bool _showScore = true;
        private bool _showError = true;

        public event EventHandler FiltersChanged;
        public event EventHandler<LogEntry> EntryAdded;

        private readonly Dispatcher _disp;

        private AppLogger()
        {
            _disp = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

            View = CollectionViewSource.GetDefaultView(Entries);
            View.Filter = FilterPredicate;
        }

        // =============================================================
        // Запись
        // =============================================================
        public void Log(LogLevel level, string message)
        {
            if (!_disp.CheckAccess())
            {
                _disp.BeginInvoke(new Action(() => Log(level, message)));
                return;
            }

            var entry = new LogEntry
            {
                Time = DateTime.Now,
                Level = level,
                Message = message
            };

            Entries.Add(entry);
            while (Entries.Count > LogLimit) Entries.RemoveAt(0);

            EntryAdded?.Invoke(this, entry);
        }

        public void Raw(string m) => Log(LogLevel.Raw, m);
        public void Info(string m) => Log(LogLevel.Info, m);
        public void State(string m) => Log(LogLevel.State, m);
        public void Press(string m) => Log(LogLevel.Press, m);
        public void Score(string m) => Log(LogLevel.Score, m);
        public void Error(string m) => Log(LogLevel.Error, m);

        // =============================================================
        // Фильтр
        // =============================================================
        private bool FilterPredicate(object obj)
        {
            if (!(obj is LogEntry e)) return true;
            switch (e.Level)
            {
                case LogLevel.Raw: return ShowRaw;
                case LogLevel.Info: return ShowInfo;
                case LogLevel.State: return ShowState;
                case LogLevel.Press: return ShowPress;
                case LogLevel.Score: return ShowScore;
                case LogLevel.Error: return ShowError;
                default: return true;
            }
        }

        public void Refresh() => View.Refresh();

        private void OnFilterChanged() => FiltersChanged?.Invoke(this, EventArgs.Empty);

        public void Clear()
        {
            Entries.Clear();
        }

        // =============================================================
        // Экспорт
        // =============================================================
        public void ExportCsv(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Time,Level,Message");
            foreach (var e in Entries)
            {
                sb.Append(EscapeCsv(e.Time.ToString("yyyy-MM-dd HH:mm:ss.fff"))).Append(',');
                sb.Append(EscapeCsv(e.Level.ToString())).Append(',');
                sb.AppendLine(EscapeCsv(e.Message));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true)); // BOM для Excel
        }

        public void ExportTxt(string path)
        {
            var sb = new StringBuilder();
            foreach (var e in Entries)
                sb.AppendLine($"[{e.Time:yyyy-MM-dd HH:mm:ss.fff}] [{e.Level}] {e.Message}");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static string EscapeCsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            bool needQuote = s.Contains(',') || s.Contains('"') ||
                             s.Contains('\n') || s.Contains('\r');
            if (!needQuote) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}