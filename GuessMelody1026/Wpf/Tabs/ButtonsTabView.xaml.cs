using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using GuessMelody.Core.Game;
using GuessMelody.Core.Models;
using GuessMelody.Core.Storage;

namespace GuessMelody.Wpf.Tabs
{
    public partial class ButtonsTabView : UserControl
    {
        private DispatcherTimer _uiTimer;
        private DispatcherTimer _lampTimer;

        private readonly ObservableCollection<ButtonSnapshot> _rows =
            new ObservableCollection<ButtonSnapshot>();

        public ButtonsTabView()
        {
            InitializeComponent();
            ButtonsGrid.ItemsSource = _rows;

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _uiTimer.Tick += (_, __) => RefreshGrid();
            _uiTimer.Start();

            _lampTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _lampTimer.Tick += (_, __) => { _lampTimer.Stop(); LampOff(); };

            Loaded += (_, __) =>
            {
                // Если сервер уже запущен — подключимся к событиям
                AttachService();
                RefreshGrid();
                UpdateServerUI();
            };

            Unloaded += (_, __) =>
            {
                _uiTimer.Stop();
                _lampTimer.Stop();
                DetachService();
            };
        }

        // =============================================================
        // Старт/стоп
        // =============================================================
        private void Start_Click(object sender, RoutedEventArgs e)
        {
            if (AppServices.IsServerRunning) { Log("Уже запущено."); return; }

            if (!int.TryParse(PortBox.Text, out var port) || port <= 0 || port > 65535)
            { Log("Некорректный порт."); return; }

            try
            {
                AppServices.StartServer(port);
                AttachService();
                Log($"Сервер запущен на UDP:{port}");
                UpdateServerUI();
                (Window.GetWindow(this) as MainWindow)?.RefreshServerStatus();
            }
            catch (Exception ex)
            {
                Log("Ошибка старта: " + ex.Message);
                AppServices.StopServer();
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            DetachService();
            AppServices.StopServer();
            Log("Сервер остановлен.");
            UpdateServerUI();
            (Window.GetWindow(this) as MainWindow)?.RefreshServerStatus();
        }

        private void UpdateServerUI()
        {
            PortBox.IsEnabled = !AppServices.IsServerRunning;
        }

        // =============================================================
        // Подписка на события ButtonService
        // =============================================================
        private void AttachService()
        {
            var svc = AppServices.ButtonService;
            if (svc == null) return;

            svc.PressReceived += OnPressReceived;
            svc.ButtonConnected += OnButtonConnected;
            svc.ButtonReconnected += OnButtonReconnected;
            svc.ButtonDisconnected += OnButtonDisconnected;
            svc.RawLog += OnRawLog;
        }

        private void DetachService()
        {
            var svc = AppServices.ButtonService;
            if (svc == null) return;

            svc.PressReceived -= OnPressReceived;
            svc.ButtonConnected -= OnButtonConnected;
            svc.ButtonReconnected -= OnButtonReconnected;
            svc.ButtonDisconnected -= OnButtonDisconnected;
            svc.RawLog -= OnRawLog;
        }

        private void OnPressReceived(object s, PressEvent e) =>
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var snap = AppServices.ButtonService.GetByMac(e.Mac);
                var label = snap?.Label ?? e.Mac;
                Log($"🔴 PRESS: {label}  seq={e.Seq}  from={e.RemoteIp}");
                LampOn(label);
                RefreshGrid();
            }));

        private void OnButtonConnected(object s, ButtonSnapshot b) =>
            Dispatcher.BeginInvoke(new Action(() =>
                Log($"🟢 Подключилась: {b.Label} ({b.Mac})")));

        private void OnButtonReconnected(object s, ButtonSnapshot b) =>
            Dispatcher.BeginInvoke(new Action(() =>
                Log($"🔗 Вернулась:    {b.Label} ({b.Mac})")));

        private void OnButtonDisconnected(object s, ButtonSnapshot b) =>
            Dispatcher.BeginInvoke(new Action(() =>
                Log($"🔌 Потеряна:     {b.Label} ({b.Mac})")));

        private void OnRawLog(object s, string m) =>
            Dispatcher.BeginInvoke(new Action(() => Log(m)));

        // =============================================================
        // Привязки
        // =============================================================
        private void LoadBindings_Click(object sender, RoutedEventArgs e)
        {
            if (!AppServices.IsServerRunning) { Log("Сначала запустите сервер."); return; }
            try
            {
                var bindings = JsonStore.Load<ButtonBindings>(AppServices.ButtonBindingsPath);
                AppServices.ButtonService.ApplyBindings(bindings.Bindings);
                Log($"Загружено привязок: {bindings.Bindings.Count}");
                RefreshGrid();
            }
            catch (Exception ex) { Log("Ошибка загрузки привязок: " + ex.Message); }
        }

        private void SaveBindings_Click(object sender, RoutedEventArgs e)
        {
            if (!AppServices.IsServerRunning) { Log("Сначала запустите сервер."); return; }
            try
            {
                AppServices.SaveButtonBindings();
                Log($"Привязки сохранены → {AppServices.ButtonBindingsPath}");
            }
            catch (Exception ex) { Log("Ошибка сохранения привязок: " + ex.Message); }
        }

        // =============================================================
        // Эмуляция
        // =============================================================
        private void Emulate_Click(object sender, RoutedEventArgs e)
        {
            if (!AppServices.IsServerRunning) { Log("Сначала запустите сервер."); return; }
            AppServices.ButtonService.EmulatePress(EmuMacBox.Text.Trim());
        }

        // =============================================================
        // Редактирование имени/слота в таблице
        // =============================================================
        private void ButtonsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (!(e.Row.Item is ButtonSnapshot snap)) return;

            // Отложенное применение — после того как DataGrid зафиксирует значение
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    AppServices.ButtonService.Bind(snap.Mac, snap.DisplayName, snap.PlayerSlot);
                }
                catch (Exception ex) { Log("Ошибка привязки: " + ex.Message); }
            }), DispatcherPriority.Background);
        }

        // =============================================================
        // Лампа
        // =============================================================
        private void LampOn(string text)
        {
            LampText.Text = text;
            LampText.Foreground = System.Windows.Media.Brushes.White;
            LampBorder.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                    .ConvertFromString("#FF3030"));
            _lampTimer.Stop();
            _lampTimer.Start();
        }

        private void LampOff()
        {
            LampText.Text = "— нет нажатий —";
            LampText.Foreground = System.Windows.Media.Brushes.Gray;
            LampBorder.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                    .ConvertFromString("#EEE"));
        }

        // =============================================================
        // Обновление таблицы
        // =============================================================
        private void RefreshGrid()
        {
            if (!AppServices.IsServerRunning) return;

            var latest = AppServices.ButtonService.GetAll();

            // Пытаемся не «моргать» — обновляем по MAC-у, а новых добавляем, ушедших удаляем.
            var byMac = latest.ToDictionary(b => b.Mac, StringComparer.OrdinalIgnoreCase);

            for (int i = _rows.Count - 1; i >= 0; i--)
                if (!byMac.ContainsKey(_rows[i].Mac))
                    _rows.RemoveAt(i);

            foreach (var b in latest)
            {
                var existing = _rows.FirstOrDefault(r =>
                    string.Equals(r.Mac, b.Mac, StringComparison.OrdinalIgnoreCase));
                if (existing == null) _rows.Add(b);
                else CopySnapshot(b, existing);
            }
        }

        private static void CopySnapshot(ButtonSnapshot src, ButtonSnapshot dst)
        {
            dst.DisplayName = src.DisplayName;
            dst.PlayerSlot = src.PlayerSlot;
            dst.LastIp = src.LastIp;
            dst.Alive = src.Alive;
            dst.FirstSeenUtc = src.FirstSeenUtc;
            dst.LastSeenUtc = src.LastSeenUtc;
            dst.PressCount = src.PressCount;
            dst.LastSeq = src.LastSeq;
            dst.LastUptimeMs = src.LastUptimeMs;
            dst.HbReceived = src.HbReceived;
            dst.HbExpected = src.HbExpected;
            dst.DeliveryPercent = src.DeliveryPercent;

            // Обновим все свойства, чтобы DataGrid перерисовался
            if (dst is System.ComponentModel.INotifyPropertyChanged inpc)
            {
                // Наш ButtonSnapshot пока не INPC — просто перезальём строку через Items.Refresh
                // Простейший путь: заменить элемент
            }
        }

        // =============================================================
        // Лог
        // =============================================================
        private void Log(string s)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => Log(s)));
                return;
            }
            LogList.Items.Add($"[{DateTime.Now:HH:mm:ss.fff}] {s}");
            if (LogList.Items.Count > 1000) LogList.Items.RemoveAt(0);
            LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
        }
    }
}