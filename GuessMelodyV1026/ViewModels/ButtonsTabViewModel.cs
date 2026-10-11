using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Windows.Input;
using System.Windows.Threading;
using GuessMelody.Buttons;
using GuessMelody.Core;
using GuessMelody.Core.Enums;
using GuessMelody.Core.Serialization;
using GuessMelody.Services;

namespace GuessMelody.ViewModels
{
    public class ButtonsTabViewModel : ViewModelBase, IDisposable
    {
        private readonly ButtonService _buttons;
        private readonly LogService _log;
        private readonly DialogService _dlg;
        private readonly DispatcherTimer _uiTimer;

        public ObservableCollection<ButtonRowViewModel> Rows { get; }
            = new ObservableCollection<ButtonRowViewModel>();

        public ButtonsTabViewModel(ButtonService buttons, LogService log, DialogService dlg)
        {
            _buttons = buttons;
            _log = log;
            _dlg = dlg;

            StartCommand = new RelayCommand(_ => StartServer(), _ => !IsRunning);
            StopCommand = new RelayCommand(_ => StopServer(), _ => IsRunning);
            SaveCommand = new RelayCommand(_ => SaveConfig());
            LoadCommand = new RelayCommand(_ => LoadConfig());
            SelectAllCommand = new RelayCommand(_ => { foreach (var r in Rows) r.IsSelected = true; });
            DeselectAllCommand = new RelayCommand(_ => { foreach (var r in Rows) r.IsSelected = false; });
            SelectAliveCommand = new RelayCommand(_ => { foreach (var r in Rows) r.IsSelected = r.Alive; });
            EmulatePressCommand = new RelayCommand(p => EmulatePress(p as string));
            SendSetServerCommand = new RelayCommand(_ => SendSetServer());
            SendGetCfgCommand = new RelayCommand(_ => SendGetCfg());
            ApplyNamesCommand = new RelayCommand(_ => ApplyNames());

            _buttons.Connected += OnButtonEvent;
            _buttons.Reconnected += OnButtonEvent;
            _buttons.Disconnected += OnButtonEvent;
            _buttons.Press += OnPress;

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _uiTimer.Tick += (_, __) =>
            {
                foreach (var r in Rows) r.RefreshTime();
                OnPropertyChanged(nameof(AliveSummary));
            };
            _uiTimer.Start();

            RefreshFromHub();
        }

        // -------- Команды --------
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand LoadCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand DeselectAllCommand { get; }
        public ICommand SelectAliveCommand { get; }
        public ICommand EmulatePressCommand { get; }
        public ICommand SendSetServerCommand { get; }
        public ICommand SendGetCfgCommand { get; }
        public ICommand ApplyNamesCommand { get; }

        // -------- Свойства --------
        private int _udpPort = 41234;
        public int UdpPort
        {
            get => _udpPort;
            set => Set(ref _udpPort, value);
        }

        private string _serverIp = "192.168.137.1";
        public string ServerIp
        {
            get => _serverIp;
            set => Set(ref _serverIp, value);
        }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            private set { Set(ref _isRunning, value); OnPropertyChanged(nameof(StatusText)); }
        }

        private string _statusText = "Сервер остановлен";
        public string StatusText
        {
            get => _statusText;
            private set => Set(ref _statusText, value);
        }

        public string LocalIpsText =>
            string.Join(", ", GetLocalIPv4().Select(ip => ip.ToString()));

        public string AliveSummary =>
            $"Живых {Rows.Count(r => r.Alive)}/{Rows.Count}";

        // -------- Логика --------


        /// <summary>Применить привязки из внешнего конфига (например, при LoadGame).</summary>
        public void ApplyExternalBindings(ButtonsConfig cfg)
        {
            if (cfg == null) return;
            _buttons.ApplyBindings(cfg);

            // Разложить имена по строкам таблицы
            foreach (var row in Rows)
            {
                var b = cfg.Bindings.FirstOrDefault(x =>
                    string.Equals(x.Mac, row.Mac, StringComparison.OrdinalIgnoreCase));
                if (b != null) row.Name = b.TeamName;
            }
            _log.Add(LogKind.System, $"Внешние привязки применены ({cfg.Bindings.Count})");
        }
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
                _log.Add(LogKind.System, "Сервер кнопок остановлен");
            }
            catch (Exception ex)
            {
                _dlg.Error("Ошибка остановки: " + ex.Message);
            }
        }

        private void OnButtonEvent(object sender, ButtonEventArgs e)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                UpsertRow(e.Button);
                OnPropertyChanged(nameof(AliveSummary));
            }));
        }

        private void OnPress(object sender, PressEventArgs e)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                UpsertRow(e.Button);
            }));
        }

        private void UpsertRow(ButtonInfo info)
        {
            var row = Rows.FirstOrDefault(r =>
                string.Equals(r.Mac, info.Mac, StringComparison.OrdinalIgnoreCase));
            if (row == null)
            {
                row = new ButtonRowViewModel(info.Mac, _buttons.GetTeamName(info.Mac));
                Rows.Add(row);
            }
            row.Update(info);
        }

        public void RefreshFromHub()
        {
            Rows.Clear();
            foreach (var info in _buttons.Hub.GetButtons())
                Rows.Add(new ButtonRowViewModel(info.Mac, _buttons.GetTeamName(info.Mac))
                {
                    // после создания заполним
                });

            // Обновим данными
            foreach (var info in _buttons.Hub.GetButtons())
            {
                var row = Rows.FirstOrDefault(r =>
                    string.Equals(r.Mac, info.Mac, StringComparison.OrdinalIgnoreCase));
                row?.Update(info);
            }
            OnPropertyChanged(nameof(AliveSummary));
        }

        private void ApplyNames()
        {
            var cfg = new ButtonsConfig();
            foreach (var r in Rows)
                cfg.Bindings.Add(new ButtonBinding
                {
                    TeamId = cfg.Bindings.Count + 1,
                    TeamName = r.Name ?? "",
                    Mac = r.Mac
                });
            _buttons.ApplyBindings(cfg);
        }

        private void SaveConfig()
        {
            ApplyNames();
            var path = _dlg.SaveFile("JSON (*.json)|*.json", "buttons.json");
            if (string.IsNullOrEmpty(path)) return;

            if (JsonStore.Save(path, _buttons.Config))
                _log.Add(LogKind.System, $"Привязки сохранены: {path}");
            else
                _dlg.Error("Не удалось сохранить привязки.");
        }

        private void LoadConfig()
        {
            var path = _dlg.OpenFile("JSON (*.json)|*.json");
            if (string.IsNullOrEmpty(path)) return;

            var cfg = JsonStore.Load<ButtonsConfig>(path);
            if (cfg == null)
            {
                _dlg.Error("Не удалось загрузить файл.");
                return;
            }

            _buttons.ApplyBindings(cfg);

            foreach (var r in Rows)
            {
                var b = cfg.Bindings.FirstOrDefault(x =>
                    string.Equals(x.Mac, r.Mac, StringComparison.OrdinalIgnoreCase));
                if (b != null) r.Name = b.TeamName;
            }

            _log.Add(LogKind.System, $"Привязки загружены: {path}");
        }

        private void EmulatePress(string mac)
        {
            if (string.IsNullOrEmpty(mac)) return;
            var info = _buttons.Hub.GetButton(mac);
            if (info == null) return;
            OnPress(this, new PressEventArgs(info,
                seq: "1", uptimeMs: 0,
                receivedUtc: DateTime.UtcNow,
                remoteIp: IPAddress.Loopback));
        }

        private void SendSetServer()
        {
            if (!IPAddress.TryParse(ServerIp, out var ip))
            {
                _dlg.Warn("Некорректный IP");
                return;
            }
            var selected = Rows.Where(r => r.IsSelected).Select(r => r.Mac).ToList();
            if (selected.Count == 0)
            {
                if (!_dlg.Confirm("Ничего не выбрано. Отправить broadcast всем?")) return;
                _buttons.SendSetServer(ip, null);
                _log.Add(LogKind.System, $"BROADCAST SETSRV|{ip}");
            }
            else
            {
                _buttons.SendSetServer(ip, selected);
                _log.Add(LogKind.System, $"SETSRV|{ip} → {string.Join(", ", selected)}");
            }
        }

        private void SendGetCfg()
        {
            var selected = Rows.Where(r => r.IsSelected).Select(r => r.Mac).ToList();
            if (selected.Count == 0)
            {
                _buttons.Hub.SendGetCfg(null);
                _log.Add(LogKind.System, "BROADCAST GETCFG");
            }
            else
            {
                _buttons.Hub.SendGetCfg(selected);
                _log.Add(LogKind.System, $"GETCFG → {string.Join(", ", selected)}");
            }
        }

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
            _buttons.Connected -= OnButtonEvent;
            _buttons.Reconnected -= OnButtonEvent;
            _buttons.Disconnected -= OnButtonEvent;
            _buttons.Press -= OnPress;

        }
    }
}