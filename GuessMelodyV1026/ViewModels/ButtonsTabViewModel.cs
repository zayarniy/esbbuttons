using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using GuessMelody.Buttons;
using GuessMelody.Core;
using GuessMelody.Core.Enums;
using GuessMelody.Core.Models;
using GuessMelody.Services;

namespace GuessMelody.ViewModels
{
    public class ButtonsTabViewModel : ViewModelBase, IDisposable
    {
        private readonly ButtonService _buttons;
        private readonly GameEngine _engine;
        private readonly LogService _log;
        private readonly DialogService _dlg;
        private readonly DispatcherTimer _uiTimer;

        // режим захвата MAC для конкретной строки
        private int? _captureForTeamId;

        public ObservableCollection<ButtonRowViewModel> Rows { get; }
            = new ObservableCollection<ButtonRowViewModel>();

        public ButtonsTabViewModel(
            ButtonService buttons,
            GameEngine engine,
            LogService log,
            DialogService dlg)
        {
            _buttons = buttons;
            _engine = engine;
            _log = log;
            _dlg = dlg;

            // ---- Команды ----
            StartCommand = new RelayCommand(_ => StartServer(), _ => !IsRunning);
            StopCommand = new RelayCommand(_ => StopServer(), _ => IsRunning);

            AddTeamCommand = new RelayCommand(_ => AddTeam(), _ => _engine.CanEditTeams);
            RemoveTeamCommand = new RelayCommand(_ => RemoveSelected(), _ => _engine.CanEditTeams);
            RenameTeamCommand = new RelayCommand(_ => ApplyNames());
            ClearMacCommand = new RelayCommand(_ => ClearSelectedMacs(), _ => _engine.CanEditTeams);

            CaptureMacCommand = new RelayCommand(p => StartCapture(p));
            CancelCaptureCommand = new RelayCommand(_ => StopCapture());

            SelectAllCommand = new RelayCommand(_ => { foreach (var r in Rows) r.IsSelected = true; });
            DeselectAllCommand = new RelayCommand(_ => { foreach (var r in Rows) r.IsSelected = false; });
            SelectAliveCommand = new RelayCommand(_ => { foreach (var r in Rows) r.IsSelected = r.Alive; });

            EmulateHbCommand = new RelayCommand(_ => EmulateHeartbeats());
            EmulatePressCommand = new RelayCommand(p => EmulatePress(p as string));

            RebootSelectedCommand = new RelayCommand(_ => RebootSelected());

            // ---- Подписки на события движка и хаба ----
            _engine.TeamsChanged += (s, e) => App.Current?.Dispatcher.BeginInvoke(new Action(RefreshRows));
            _engine.MacSeen += OnMacSeen;

            _buttons.Connected += OnHubButtonEvent;
            _buttons.Reconnected += OnHubButtonEvent;
            _buttons.Disconnected += OnHubButtonEvent;
            _buttons.Press += OnHubPress;
            _buttons.Heartbeat += OnHubButtonEvent;

            // таймер для «тишины»
            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _uiTimer.Tick += (_, __) =>
            {
                foreach (var r in Rows) r.RefreshTime();
                OnPropertyChanged(nameof(AliveSummary));
            };
            _uiTimer.Start();

            RefreshRows();
        }

        // ---------------------------------------------------------
        // Публичные свойства и команды
        // ---------------------------------------------------------
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand AddTeamCommand { get; }
        public ICommand RemoveTeamCommand { get; }
        public ICommand RenameTeamCommand { get; }
        public ICommand ClearMacCommand { get; }
        public ICommand CaptureMacCommand { get; }
        public ICommand CancelCaptureCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand DeselectAllCommand { get; }
        public ICommand SelectAliveCommand { get; }
        public ICommand EmulateHbCommand { get; }
        public ICommand EmulatePressCommand { get; }
        public ICommand RebootSelectedCommand { get; }

        private int _udpPort = 41234;
        public int UdpPort { get => _udpPort; set => Set(ref _udpPort, value); }

        private string _serverIp = "192.168.137.1";
        public string ServerIp { get => _serverIp; set => Set(ref _serverIp, value); }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            private set { Set(ref _isRunning, value); OnPropertyChanged(nameof(StatusText)); }
        }

        private string _statusText = "Сервер остановлен";
        public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

        public string LocalIpsText =>
            string.Join(", ", GetLocalIPv4().Select(ip => ip.ToString()));

        public string AliveSummary =>
            $"Живых {Rows.Count(r => r.Alive)}/{Rows.Count}";

        // ---- Захват ----
        private bool _isCapturing;
        public bool IsCapturing
        {
            get => _isCapturing;
            private set { Set(ref _isCapturing, value); OnPropertyChanged(nameof(CaptureStatusText)); }
        }

        private string _captureTargetName;
        public string CaptureTargetName
        {
            get => _captureTargetName;
            private set { Set(ref _captureTargetName, value); OnPropertyChanged(nameof(CaptureStatusText)); }
        }

        public string CaptureStatusText =>
            IsCapturing
                ? $"🎯 Жду нажатие/HB от новой кнопки → «{CaptureTargetName}»"
                : "";

        // автосоздание команды при HB от неизвестного MAC
        private bool _autoCreateUnknown = true;
        public bool AutoCreateUnknown
        {
            get => _autoCreateUnknown;
            set => Set(ref _autoCreateUnknown, value);
        }

        // ---------------------------------------------------------
        // Сервер
        // ---------------------------------------------------------
        private void StartServer()
        {
            try
            {
                _buttons.Start();
                IsRunning = true;
                StatusText = $"Сервер запущен на UDP:{UdpPort}";
                _log.Add(LogKind.System, $"Сервер кнопок запущен на UDP:{UdpPort}");
            }
            catch (Exception ex)
            {
                _dlg.Error("Не удалось запустить сервер: " + ex.Message);
            }
        }

        private void StopServer()
        {
            try
            {
                _buttons.Stop();
                IsRunning = false;
                StatusText = "Сервер остановлен";
            }
            catch (Exception ex)
            {
                _dlg.Error("Ошибка остановки: " + ex.Message);
            }
        }

        // ---------------------------------------------------------
        // Синхронизация UI с движком
        // ---------------------------------------------------------
        public void RefreshRows()
        {
            // Простейший вариант — пересобрать список строк, но сохранить IsSelected
            var selectedIds = Rows.Where(r => r.IsSelected).Select(r => r.Id).ToHashSet();

            Rows.Clear();

            foreach (var t in _engine.Teams)
            {
                var row = new ButtonRowViewModel(t.Id, t.Name, t.Mac);
                row.IsSelected = selectedIds.Contains(t.Id);

                // Подтянуть данные от хаба, если MAC реальный
                if (!row.IsVirtual)
                {
                    var info = _buttons.Hub.GetButton(t.Mac);
                    if (info != null) row.UpdateHub(info);
                }

                Rows.Add(row);
            }

            OnPropertyChanged(nameof(AliveSummary));
        }

        // ---------------------------------------------------------
        // Работа с составом
        // ---------------------------------------------------------
        private void AddTeam()
        {
            _engine.AddTeam();
            _log.Add(LogKind.System, "Добавлена команда");
            RefreshRows();
        }

        private void RemoveSelected()
        {
            var toRemove = Rows.Where(r => r.IsSelected).Select(r => r.Id).ToList();
            if (toRemove.Count == 0)
            {
                _dlg.Info("Отметьте команды для удаления.");
                return;
            }
            if (!_dlg.Confirm($"Удалить {toRemove.Count} команд?")) return;

            foreach (var id in toRemove) _engine.RemoveTeam(id);
            RefreshRows();
        }

        private void ApplyNames()
        {
            foreach (var row in Rows)
            {
                _engine.RenameTeam(row.Id, row.Name);
                // MAC вводится в TextBox — обновим, если отличается
                var current = _engine.Teams.FirstOrDefault(t => t.Id == row.Id)?.Mac;
                if (!string.Equals(current, row.Mac, StringComparison.OrdinalIgnoreCase))
                {
                    bool ok = _engine.AssignMac(row.Id, row.Mac, force: false);
                    if (!ok)
                    {
                        _dlg.Warn(
                            $"MAC {row.Mac} уже занят. Принудительно перепривязать?\n" +
                            $"Это отвяжет старую команду.");
                        _engine.AssignMac(row.Id, row.Mac, force: true);
                    }
                }
            }
            RefreshRows();
        }

        private void ClearSelectedMacs()
        {
            var selected = Rows.Where(r => r.IsSelected).ToList();
            if (selected.Count == 0) { _dlg.Info("Отметьте команды."); return; }
            if (!_dlg.Confirm($"Отвязать MAC у {selected.Count} команд?")) return;

            foreach (var row in selected)
                _engine.ClearMac(row.Id);

            RefreshRows();
        }

        private void StartCapture(object parameter)
        {
            int id;
            if (parameter is int i) id = i;
            else if (parameter is string s && int.TryParse(s, out var n)) id = n;
            else if (parameter is ButtonRowViewModel row) id = row.Id;
            else if (Rows.Count > 0 && Rows.Any(r => r.IsSelected))
                id = Rows.First(r => r.IsSelected).Id;
            else { _dlg.Info("Выберите команду."); return; }

            var team = _engine.Teams.FirstOrDefault(t => t.Id == id);
            if (team == null) return;

            _captureForTeamId = id;
            CaptureTargetName = team.Name;
            IsCapturing = true;
            _log.Add(LogKind.System, $"🎯 Захват MAC для «{team.Name}» (Id={id})");
        }

        private void StopCapture()
        {
            _captureForTeamId = null;
            IsCapturing = false;
            CaptureTargetName = null;
            _log.Add(LogKind.System, "Захват отменён.");
        }

        // ---------------------------------------------------------
        // Обработка MacSeen — захват и автосоздание
        // ---------------------------------------------------------
        private void OnMacSeen(object sender, string mac)
        {
            if (string.IsNullOrWhiteSpace(mac)) return;

            App.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                // 1) Явный захват для конкретной команды
                if (_captureForTeamId.HasValue)
                {
                    var id = _captureForTeamId.Value;
                    var team = _engine.Teams.FirstOrDefault(t => t.Id == id);
                    if (team == null) { StopCapture(); return; }

                    bool ok = _engine.AssignMac(id, mac, force: false);
                    if (!ok)
                    {
                        var busy = _engine.FindTeamByMacExcept(mac, id);
                        var res = MessageBox.Show(
                            $"MAC {mac} уже привязан к «{busy?.Name}».\n" +
                            $"Перепривязать к «{team.Name}»?",
                            "Подтверждение",
                            MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (res == MessageBoxResult.Yes)
                            _engine.AssignMac(id, mac, force: true);
                    }

                    if (_engine.Teams.FirstOrDefault(t => t.Id == id)?.Mac == mac)
                    {
                        _log.Add(LogKind.System, $"✅ Захвачен MAC {mac} → «{team.Name}»");
                        StopCapture();
                        RefreshRows();
                    }
                    return;
                }

                // 2) Автосоздание команды при HB от неизвестного MAC
                if (AutoCreateUnknown && _engine.FindTeamByMac(mac) == null)
                {
                    _engine.AddTeam(null, mac);
                    _log.Add(LogKind.System, $"🆕 Автосоздана команда для {mac}");
                    RefreshRows();
                }
            }));
        }

        // ---------------------------------------------------------
        // Эмуляция
        // ---------------------------------------------------------
        private void EmulateHeartbeats()
        {
            var targets = Rows.Where(r => r.IsSelected).ToList();
            if (targets.Count == 0)
                targets = Rows.ToList();

            foreach (var row in targets)
                _engine.OnHeartbeat(row.Mac);

            _log.Add(LogKind.System, $"💓 Эмуляция HB для {targets.Count} команд");
        }

        private void EmulatePress(string mac)
        {
            if (string.IsNullOrEmpty(mac)) return;
            _engine.OnPlayerPress(mac, DateTime.UtcNow, "test", 0);
            _log.Add(LogKind.System, $"🎮 Эмуляция нажатия: {mac}");
        }

        private void RebootSelected()
        {
            var macs = Rows.Where(r => r.IsSelected && !r.IsVirtual).Select(r => r.Mac).ToList();
            if (macs.Count == 0)
            {
                _dlg.Info("Отметьте команды с реальными MAC.");
                return;
            }
            if (!_dlg.Confirm($"Перезагрузить {macs.Count} кнопок?")) return;

            _buttons.SendReboot(macs);
            _log.Add(LogKind.System, $"🔄 REBOOT для {macs.Count} кнопок");
        }

        // ---------------------------------------------------------
        // Обработчики событий хаба
        // ---------------------------------------------------------
        private void OnHubButtonEvent(object sender, ButtonEventArgs e)
        {
            App.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                var mac = e.Button?.Mac;
                if (string.IsNullOrEmpty(mac)) return;

                var row = Rows.FirstOrDefault(r =>
                    string.Equals(r.Mac, mac, StringComparison.OrdinalIgnoreCase));
                if (row != null) row.UpdateHub(e.Button);

                OnPropertyChanged(nameof(AliveSummary));
            }));
        }

        private void OnHubPress(object sender, PressEventArgs e)
        {
            App.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                var mac = e.Button?.Mac;
                if (string.IsNullOrEmpty(mac)) return;

                var row = Rows.FirstOrDefault(r =>
                    string.Equals(r.Mac, mac, StringComparison.OrdinalIgnoreCase));
                if (row != null) row.UpdateHub(e.Button);
            }));
        }

        // ---------------------------------------------------------
        private static IPAddress[] GetLocalIPv4()
        {
            try
            {
                return Dns.GetHostAddresses(Dns.GetHostName())
                          .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                          .ToArray();
            }
            catch { return new IPAddress[0]; }
        }

        public void Dispose()
        {
            _uiTimer?.Stop();

            _engine.TeamsChanged -= (s, e) => { };   // no-op, лямбда не отписывается
            _engine.MacSeen -= OnMacSeen;

            _buttons.Connected -= OnHubButtonEvent;
            _buttons.Reconnected -= OnHubButtonEvent;
            _buttons.Disconnected -= OnHubButtonEvent;
            _buttons.Press -= OnHubPress;
            _buttons.Heartbeat -= OnHubButtonEvent;
        }
    }
}