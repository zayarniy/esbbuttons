using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GuessMelody.Core.Models;
using GuessMelody.Wpf.Logging;
using GuessMelody.Wpf.Game;
using GuessMelody.Core.Game;

namespace GuessMelody.Wpf.Tabs
{
    public partial class SettingsTabView : UserControl
    {
        private readonly ObservableCollection<SelectableSnapshot> _selectable =
            new ObservableCollection<SelectableSnapshot>();
        private DispatcherTimer _refreshTimer;

        public SettingsTabView()
        {
            InitializeComponent();
            SelectGrid.ItemsSource = _selectable;

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _refreshTimer.Tick += (_, __) => RefreshSelectable();

            Loaded += (_, __) =>
            {
                _refreshTimer.Start();
                AttachAckHandler();
                RefreshSelectable();
            };
            Unloaded += (_, __) =>
            {
                _refreshTimer.Stop();
                DetachAckHandler();
            };
        }

        // =============================================================
        // Смена сервера
        // =============================================================
        private void SendServerAll_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetIp(out var ip)) return;
            AppServices.Hub.SendSetServer(ip, null);
            Log($"BROADCAST SETSRV|{ip}");
        }

        private void SendServerSelected_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetIp(out var ip)) return;
            var macs = SelectedMacs();
            if (macs.Count == 0) { Log("Не выбрано ни одной кнопки."); return; }
            AppServices.Hub.SendSetServer(ip, macs);
            Log($"SETSRV|{ip} → {string.Join(", ", macs)}");
        }

        private bool TryGetIp(out IPAddress ip)
        {
            ip = null;
            if (!IPAddress.TryParse(NewServerIpBox.Text, out ip))
            {
                MessageBox.Show("Некорректный IP", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (!AppServices.IsServerRunning)
            {
                MessageBox.Show("Сначала запустите сервер (вкладка «Кнопки» или «Запуск»).",
                    "Инфо", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
            return true;
        }

        // =============================================================
        // Смена Wi-Fi / REBOOT
        // =============================================================
        private void SendWifiAll_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NewSsidBox.Text))
            {
                MessageBox.Show("Введите SSID", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!AppServices.IsServerRunning) { Log("Сервер не запущен."); return; }

            AppServices.Hub.SendSetWifi(NewSsidBox.Text, NewPassBox.Text, null);
            Log($"BROADCAST SETWIFI|{NewSsidBox.Text}|***");
        }

        private void SendReboot_Click(object sender, RoutedEventArgs e)
        {
            if (!AppServices.IsServerRunning) { Log("Сервер не запущен."); return; }
            var macs = SelectedMacs();
            if (macs.Count > 0)
                AppServices.Hub.SendReboot(macs);
            else
                AppServices.Hub.SendReboot(null);   // broadcast
            Log($"REBOOT → {(macs.Count > 0 ? string.Join(", ", macs) : "BROADCAST")}");
        }

        // =============================================================
        // Выбор кнопок
        // =============================================================
        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var s in _selectable) s.IsSelected = true;
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var s in _selectable) s.IsSelected = false;
        }

        private void SelectAlive_Click(object sender, RoutedEventArgs e)
        {
            foreach (var s in _selectable) s.IsSelected = s.Alive;
        }

        private List<string> SelectedMacs() =>
            _selectable.Where(s => s.IsSelected).Select(s => s.Mac).ToList();

        private void RefreshSelectable()
        {
            if (!AppServices.IsServerRunning) return;

            var latest = AppServices.ButtonService.GetAll();
            var byMac = latest.ToDictionary(b => b.Mac, StringComparer.OrdinalIgnoreCase);

            for (int i = _selectable.Count - 1; i >= 0; i--)
                if (!byMac.ContainsKey(_selectable[i].Mac))
                    _selectable.RemoveAt(i);

            foreach (var b in latest)
            {
                var existing = _selectable.FirstOrDefault(s =>
                    string.Equals(s.Mac, b.Mac, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    _selectable.Add(new SelectableSnapshot
                    {
                        Mac = b.Mac,
                        DisplayName = b.Label,
                        LastIp = b.LastIp,
                        Alive = b.Alive
                    });
                }
                else
                {
                    existing.DisplayName = b.Label;
                    existing.LastIp = b.LastIp;
                    existing.Alive = b.Alive;
                }
            }
        }

        // =============================================================
        // ACK от кнопок
        // =============================================================
        private void AttachAckHandler()
        {
            if (AppServices.Hub != null)
                AppServices.Hub.CommandAck += Hub_CommandAck;
        }

        private void DetachAckHandler()
        {
            if (AppServices.Hub != null)
                AppServices.Hub.CommandAck -= Hub_CommandAck;
        }

        private void Hub_CommandAck(object s, AckEventArgs e) =>
            Dispatcher.BeginInvoke(new Action(() =>
                Log($"[{e.From}] {e.Payload}")));

        // =============================================================
        private void Log(string s)
        {
            AckLogList.Items.Add($"[{DateTime.Now:HH:mm:ss.fff}] {s}");
            if (AckLogList.Items.Count > 500) AckLogList.Items.RemoveAt(0);
            AckLogList.ScrollIntoView(AckLogList.Items[AckLogList.Items.Count - 1]);
            AppLogger.Instance.Info("[Settings] " + s);
        }
    }

    // =============================================================
    public class SelectableSnapshot : INotifyPropertyChanged
    {
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        public string Mac { get; set; }
        public string DisplayName { get; set; }
        public string LastIp { get; set; }
        public bool Alive { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string p) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}