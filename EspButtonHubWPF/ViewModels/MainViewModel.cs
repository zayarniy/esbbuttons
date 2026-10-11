using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EspButtonDiag.Wpf.Models;
using EspButtonDiag.Wpf.Utils;
using Microsoft.Win32;
using EspButtonDiag;
using static EspButtonDiag.ButtonInfo;

namespace EspButtonDiag.Wpf.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly EspButtonHub _hub;
        private readonly Dispatcher _disp;
        private DispatcherTimer _uiTimer;
        private DispatcherTimer _lampTimer;

        // ---------- Коллекции ----------
        public ObservableCollection<ButtonRowViewModel> Buttons { get; }
            = new ObservableCollection<ButtonRowViewModel>();

        public ObservableCollection<LogEntry> Log { get; }
            = new ObservableCollection<LogEntry>();

        public ICollectionView LogView { get; }

        public ObservableCollection<RoundRecord> Rounds { get; }
            = new ObservableCollection<RoundRecord>();

        public int LogLimit { get; set; } = 10000;

        // ---------- Текущий раунд ----------
        private int _currentRoundNumber = 0;
        private DateTime? _currentRoundStartUtc = null;
        private readonly HashSet<string> _pressedInCurrentRound =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public string CurrentRoundText =>
            _currentRoundStartUtc == null
                ? "Раунд не запущен"
                : $"Раунд #{_currentRoundNumber}  (нажали: {_pressedInCurrentRound.Count})";

        // ---------- Лампа-индикатор нажатия ----------
        private string _lampMac = "";
        private bool _lampOn = false;
        public string LampMac { get => _lampMac; private set { _lampMac = value; OnPropertyChanged(); } }
        public bool LampOn { get => _lampOn; private set { _lampOn = value; OnPropertyChanged(); } }

        private readonly Queue<string> _lampQueue = new Queue<string>();

        // ---------- Фильтры журнала ----------
        public bool FilterShowRaw { get => _filterRaw; set { _filterRaw = value; LogView?.Refresh(); OnPropertyChanged(); } }
        public bool FilterShowConnect { get => _filterConnect; set { _filterConnect = value; LogView?.Refresh(); OnPropertyChanged(); } }
        public bool FilterShowReconnect { get => _filterReconnect; set { _filterReconnect = value; LogView?.Refresh(); OnPropertyChanged(); } }
        public bool FilterShowDisconnect { get => _filterDisconnect; set { _filterDisconnect = value; LogView?.Refresh(); OnPropertyChanged(); } }
        public bool FilterShowPress { get => _filterPress; set { _filterPress = value; LogView?.Refresh(); OnPropertyChanged(); } }
        public bool FilterShowSystem { get => _filterSystem; set { _filterSystem = value; LogView?.Refresh(); OnPropertyChanged(); } }
        public bool FilterShowError { get => _filterError; set { _filterError = value; LogView?.Refresh(); OnPropertyChanged(); } }

        private bool _filterRaw = false;
        private bool _filterConnect = true;
        private bool _filterReconnect = true;
        private bool _filterDisconnect = true;
        private bool _filterPress = true;
        private bool _filterSystem = true;
        private bool _filterError = true;

        // ---------- Свойства UI ----------
        private string _statusText = "Остановлено";
        public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }

        private bool _isRunning;
        public bool IsRunning { get => _isRunning; set { _isRunning = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); } }

        private int _listenPort = 41234;
        public int ListenPort { get => _listenPort; set { _listenPort = value; OnPropertyChanged(); } }

        private int _ackPort = 41235;
        public int AckPort { get => _ackPort; set { _ackPort = value; OnPropertyChanged(); } }

        private int _heartbeatTimeoutSec = 30;
        public int HeartbeatTimeoutSec { get => _heartbeatTimeoutSec; set { _heartbeatTimeoutSec = value; OnPropertyChanged(); } }

        // ---------- Команды ----------
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand ClearLogCommand { get; }
        public ICommand ClearButtonsCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand ExportTxtCommand { get; }
        public ICommand StartRoundCommand { get; }
        public ICommand FinishRoundCommand { get; }
        public ICommand ClearRoundsCommand { get; }

        public MainViewModel()
        {
            _disp = Application.Current.Dispatcher;

            StartCommand = new RelayCommand(_ => StartServer(), _ => !IsRunning);
            StopCommand = new RelayCommand(_ => StopServer(), _ => IsRunning);
            ClearLogCommand = new RelayCommand(_ => Log.Clear());
            ClearButtonsCommand = new RelayCommand(_ => { Buttons.Clear(); }, _ => !IsRunning);
            ExportCsvCommand = new RelayCommand(_ => ExportLog(true));
            ExportTxtCommand = new RelayCommand(_ => ExportLog(false));
            StartRoundCommand = new RelayCommand(_ => StartRound());
            FinishRoundCommand = new RelayCommand(_ => FinishRound());
            ClearRoundsCommand = new RelayCommand(_ => Rounds.Clear());

            // Фильтр журнала
            LogView = CollectionViewSource.GetDefaultView(Log);
            LogView.Filter = LogFilterPredicate;

            _hub = new EspButtonHub(_listenPort, _ackPort,
                heartbeatTimeout: TimeSpan.FromSeconds(_heartbeatTimeoutSec));

            _hub.RawLog += Hub_RawLog;
            _hub.ButtonConnected += Hub_ButtonConnected;
            _hub.ButtonReconnected += Hub_ButtonReconnected;
            _hub.ButtonDisconnected += Hub_ButtonDisconnected;
            _hub.PressReceived += Hub_PressReceived;

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _uiTimer.Tick += (_, __) => { foreach (var b in Buttons) b.RefreshTime(); };
            _uiTimer.Start();

            // Лампа: гасим через 600 мс после последнего нажатия
            _lampTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _lampTimer.Tick += (_, __) =>
            {
                _lampTimer.Stop();
                if (_lampQueue.Count > 0)
                {
                    LampMac = _lampQueue.Dequeue();
                    // Ещё нажатия в очереди — перезапустим таймер, оставим лампу
                    _lampTimer.Start();
                }
                else
                {
                    LampOn = false;
                }
            };
        }

        // =============================================================
        // Фильтр журнала
        // =============================================================
        private bool LogFilterPredicate(object item)
        {
            if (!(item is LogEntry e)) return true;
            switch (e.Kind)
            {
                case LogKind.Raw: return FilterShowRaw;
                case LogKind.Connect: return FilterShowConnect;
                case LogKind.Reconnect: return FilterShowReconnect;
                case LogKind.Disconnect: return FilterShowDisconnect;
                case LogKind.Press: return FilterShowPress;
                case LogKind.System: return FilterShowSystem;
                case LogKind.Error: return FilterShowError;
                default: return true;
            }
        }

        // =============================================================
        // Экспорт журнала
        // =============================================================
        private void ExportLog(bool asCsv)
        {
            var dlg = new SaveFileDialog
            {
                Filter = asCsv ? "CSV файл (*.csv)|*.csv" : "Текстовый файл (*.txt)|*.txt",
                FileName = $"EspButtonLog_{DateTime.Now:yyyyMMdd_HHmmss}." + (asCsv ? "csv" : "txt")
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                if (asCsv)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Time,Kind,Message");
                    foreach (var e in Log)
                    {
                        sb.Append(EscapeCsv(e.Time.ToString("yyyy-MM-dd HH:mm:ss.fff"))).Append(',');
                        sb.Append(EscapeCsv(e.Kind.ToString())).Append(',');
                        sb.AppendLine(EscapeCsv(e.Message));
                    }
                    File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                }
                else
                {
                    var sb = new StringBuilder();
                    foreach (var e in Log)
                        sb.AppendLine($"[{e.Time:yyyy-MM-dd HH:mm:ss.fff}] [{e.Kind}] {e.Message}");
                    File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                }

                AddLog(LogKind.System, $"Журнал экспортирован: {dlg.FileName} ({Log.Count} записей)");
            }
            catch (Exception ex)
            {
                AddLog(LogKind.Error, $"Ошибка экспорта: {ex.Message}");
                MessageBox.Show(ex.Message, "Ошибка экспорта",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string EscapeCsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            bool needQuote = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
            if (!needQuote) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        // =============================================================
        // Раунды
        // =============================================================
        private void StartRound()
        {
            _currentRoundNumber++;
            _currentRoundStartUtc = DateTime.UtcNow;
            _pressedInCurrentRound.Clear();
            OnPropertyChanged(nameof(CurrentRoundText));
            AddLog(LogKind.System, $"▶ Старт раунда #{_currentRoundNumber}");
        }

        private void FinishRound()
        {
            if (_currentRoundStartUtc == null)
            {
                AddLog(LogKind.System, "Раунд не запущен — нечего завершать.");
                return;
            }
            _currentRoundStartUtc = null;
            OnPropertyChanged(nameof(CurrentRoundText));
            AddLog(LogKind.System, $"■ Раунд #{_currentRoundNumber} завершён");
        }

        // =============================================================
        // Запуск/остановка
        // =============================================================
        private void StartServer()
        {
            if (IsRunning) return;
            try
            {
                _hub.Start();
                IsRunning = true;
                StatusText = $"Запущено. UDP:{_listenPort} → ACK:{_ackPort}";
                AddLog(LogKind.System,
                    $"Сервер запущен на UDP:{_listenPort}, ACK:{_ackPort}, " +
                    $"таймаут HB {_heartbeatTimeoutSec} с");
            }
            catch (Exception ex)
            {
                StatusText = "Ошибка запуска";
                AddLog(LogKind.Error, $"Start error: {ex.Message}");
                MessageBox.Show(ex.Message, "Ошибка запуска",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopServer()
        {
            try
            {
                _hub.Stop();
                IsRunning = false;
                StatusText = "Остановлено";
                AddLog(LogKind.System, "Сервер остановлен.");
            }
            catch (Exception ex)
            {
                AddLog(LogKind.Error, $"Stop error: {ex.Message}");
            }
        }

        // =============================================================
        // События EspButtonHub
        // =============================================================
        private void Hub_RawLog(object sender, string line) =>
            _disp.BeginInvoke(new Action(() => AddLog(LogKind.Raw, line)));

        private void Hub_ButtonConnected(object sender, ButtonEventArgs e) =>
            _disp.BeginInvoke(new Action(() =>
            {
                UpsertButton(e.Button);
                AddLog(LogKind.Connect, $"НОВАЯ КНОПКА: {e.Button.Mac}  ip={e.Button.LastIp}");
                UpdateStatus();
            }));

        private void Hub_ButtonReconnected(object sender, ButtonEventArgs e) =>
            _disp.BeginInvoke(new Action(() =>
            {
                UpsertButton(e.Button);
                AddLog(LogKind.Reconnect, $"ВЕРНУЛАСЬ: {e.Button.Mac}  ip={e.Button.LastIp}");
                UpdateStatus();
            }));

        private void Hub_ButtonDisconnected(object sender, ButtonEventArgs e) =>
            _disp.BeginInvoke(new Action(() =>
            {
                UpsertButton(e.Button);
                AddLog(LogKind.Disconnect,
                    $"ПОТЕРЯНА: {e.Button.Mac}  (тишина {e.Button.TimeSinceLastSeen.TotalSeconds:F1} с)");
                UpdateStatus();
            }));

        private void Hub_PressReceived(object sender, PressEventArgs e) =>
            _disp.BeginInvoke(new Action(() =>
            {
                UpsertButton(e.Button);

                AddLog(LogKind.Press,
                    $"НАЖАТИЕ: {e.Button.Mac}  seq={e.Seq}  uptime={e.UptimeMs} ms  " +
                    $"from={e.RemoteIp}");

                // Лампа-индикатор
                _lampQueue.Enqueue(e.Button.Mac);
                if (!LampOn)
                {
                    LampOn = true;
                    LampMac = _lampQueue.Dequeue();
                }
                _lampTimer.Stop();
                _lampTimer.Start();

                // Раунды
                if (_currentRoundStartUtc != null)
                {
                    if (_pressedInCurrentRound.Add(e.Button.Mac))
                    {
                        Rounds.Add(new RoundRecord
                        {
                            Round = _currentRoundNumber,
                            Mac = e.Button.Mac,
                            Seq = e.Seq,
                            ReceivedUtc = e.ReceivedUtc,
                            UptimeMs = e.UptimeMs,
                            ReceivedIp = e.RemoteIp.ToString()
                        });
                        OnPropertyChanged(nameof(CurrentRoundText));
                    }
                }

                UpdateStatus();
            }));

        // =============================================================
        private void UpsertButton(ButtonInfo info)
        {
            var row = Buttons.FirstOrDefault(b =>
                string.Equals(b.Mac, info.Mac, StringComparison.OrdinalIgnoreCase));

            if (row == null)
            {
                row = new ButtonRowViewModel(info.Mac);
                Buttons.Add(row);
            }
            row.Update(info);
        }

        private void UpdateStatus()
        {
            int alive = Buttons.Count(b => b.Alive);
            StatusText = $"Живых {alive}/{Buttons.Count}  •  UDP:{_listenPort}";
        }

        private void AddLog(LogKind kind, string message)
        {
            // Автоматически отключаем фильтр Raw, если пользователь включил его и тонет в потоке?
            // Нет — оставляем как есть. Если нужен «bubble-up» в UI, сработает LogView.Refresh.
            Log.Add(new LogEntry(DateTime.Now, kind, message));
            while (Log.Count > LogLimit) Log.RemoveAt(0);
        }

        public void Dispose()
        {
            _uiTimer?.Stop();
            _lampTimer?.Stop();
            try { _hub?.Dispose(); } catch { }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string p = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    // ----------------------------------------------------------------
    public enum LogKind
    {
        Raw, Connect, Reconnect, Disconnect, Press, System, Error
    }

    public class LogEntry
    {
        public DateTime Time { get; }
        public LogKind Kind { get; }
        public string Message { get; }
        public string TimeText => Time.ToString("HH:mm:ss.fff");
        public string KindText => Kind.ToString();

        public LogEntry(DateTime t, LogKind k, string m) { Time = t; Kind = k; Message = m; }
    }

    // ----------------------------------------------------------------
    public class RoundRecord
    {
        public int Round { get; set; }
        public string Mac { get; set; }
        public string Seq { get; set; }
        public long UptimeMs { get; set; }
        public string ReceivedIp { get; set; }
        public DateTime ReceivedUtc { get; set; }

        // Локальное время (для отображения)
        public string ReceivedLocal =>
            ReceivedUtc.ToLocalTime().ToString("HH:mm:ss.fff");
    }
}