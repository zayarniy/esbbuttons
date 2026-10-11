using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Data;
using System.Windows.Input;
using GuessMelody.Core;
using GuessMelody.Core.Enums;
using GuessMelody.Services;

namespace GuessMelody.ViewModels
{
    public class LogTabViewModel : ViewModelBase
    {
        private readonly LogService _log;
        private readonly DialogService _dlg;
        private readonly ObservableCollection<LogEntry> _source;

        public ICollectionView View { get; }

        public LogTabViewModel(LogService log, DialogService dlg)
        {
            _log = log;
            _dlg = dlg;
            _source = _log.Entries;
            View = CollectionViewSource.GetDefaultView(_source);
            View.Filter = FilterPredicate;

            SaveCsvCommand = new RelayCommand(_ => SaveCsv());
            SaveTxtCommand = new RelayCommand(_ => SaveTxt());
            ClearCommand = new RelayCommand(_ => _log.Clear());
        }

        public ICommand SaveCsvCommand { get; }
        public ICommand SaveTxtCommand { get; }
        public ICommand ClearCommand { get; }

        // -------- Фильтры --------
        private bool _filterRaw = false;
        public bool FilterRaw { get => _filterRaw; set { if (Set(ref _filterRaw, value)) View.Refresh(); } }

        private bool _filterConnect = true;
        public bool FilterConnect { get => _filterConnect; set { if (Set(ref _filterConnect, value)) View.Refresh(); } }

        private bool _filterReconnect = true;
        public bool FilterReconnect { get => _filterReconnect; set { if (Set(ref _filterReconnect, value)) View.Refresh(); } }

        private bool _filterDisconnect = true;
        public bool FilterDisconnect { get => _filterDisconnect; set { if (Set(ref _filterDisconnect, value)) View.Refresh(); } }

        private bool _filterPress = true;
        public bool FilterPress { get => _filterPress; set { if (Set(ref _filterPress, value)) View.Refresh(); } }

        private bool _filterHb = false;
        public bool FilterHb { get => _filterHb; set { if (Set(ref _filterHb, value)) View.Refresh(); } }

        private bool _filterSystem = true;
        public bool FilterSystem { get => _filterSystem; set { if (Set(ref _filterSystem, value)) View.Refresh(); } }

        private bool _filterError = true;
        public bool FilterError { get => _filterError; set { if (Set(ref _filterError, value)) View.Refresh(); } }

        private bool _filterGame = true;
        public bool FilterGame { get => _filterGame; set { if (Set(ref _filterGame, value)) View.Refresh(); } }

        private bool _filterAudio = true;
        public bool FilterAudio { get => _filterAudio; set { if (Set(ref _filterAudio, value)) View.Refresh(); } }

        private bool _filterScore = true;
        public bool FilterScore { get => _filterScore; set { if (Set(ref _filterScore, value)) View.Refresh(); } }

        private bool _filterHttp = true;
        public bool FilterHttp { get => _filterHttp; set { if (Set(ref _filterHttp, value)) View.Refresh(); } }

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set { if (Set(ref _searchText, value)) View.Refresh(); }
        }

        private bool _autoScroll = true;
        public bool AutoScroll
        {
            get => _autoScroll;
            set => Set(ref _autoScroll, value);
        }

        private bool FilterPredicate(object item)
        {
            if (!(item is LogEntry e)) return true;

            switch (e.Kind)
            {
                case LogKind.Raw: if (!FilterRaw) return false; break;
                case LogKind.Connect: if (!FilterConnect) return false; break;
                case LogKind.Reconnect: if (!FilterReconnect) return false; break;
                case LogKind.Disconnect: if (!FilterDisconnect) return false; break;
                case LogKind.Press: if (!FilterPress) return false; break;
                case LogKind.Hb: if (!FilterHb) return false; break;
                case LogKind.System: if (!FilterSystem) return false; break;
                case LogKind.Error: if (!FilterError) return false; break;
                case LogKind.Game: if (!FilterGame) return false; break;
                case LogKind.Audio: if (!FilterAudio) return false; break;
                case LogKind.Score: if (!FilterScore) return false; break;
                case LogKind.Http: if (!FilterHttp) return false; break;
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var s = SearchText.Trim();
                if (e.Message?.IndexOf(s, StringComparison.OrdinalIgnoreCase) < 0 &&
                    e.KindText?.IndexOf(s, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            return true;
        }

        // -------- Экспорт --------
        private void SaveCsv()
        {
            var path = _dlg.SaveFile("CSV файл (*.csv)|*.csv",
                $"GuessMelody_Log_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Time,Kind,Message");
                foreach (var e in _source)
                {
                    sb.Append(EscapeCsv(e.Time.ToString("yyyy-MM-dd HH:mm:ss.fff"))).Append(',');
                    sb.Append(EscapeCsv(e.Kind.ToString())).Append(',');
                    sb.AppendLine(EscapeCsv(e.Message));
                }
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                _log.Add(LogKind.System, $"Журнал экспортирован: {path}");
            }
            catch (Exception ex)
            {
                _dlg.Error("Ошибка экспорта: " + ex.Message);
            }
        }

        private void SaveTxt()
        {
            var path = _dlg.SaveFile("Текстовый файл (*.txt)|*.txt",
                $"GuessMelody_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var sb = new StringBuilder();
                foreach (var e in _source)
                    sb.AppendLine($"[{e.Time:yyyy-MM-dd HH:mm:ss.fff}] [{e.Kind}] {e.Message}");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                _log.Add(LogKind.System, $"Журнал экспортирован: {path}");
            }
            catch (Exception ex)
            {
                _dlg.Error("Ошибка экспорта: " + ex.Message);
            }
        }

        private static string EscapeCsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            bool needQuote = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
            return needQuote ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
    }
}