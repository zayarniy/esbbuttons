using GuessMelody.Core.Game;
using GuessMelody.Core.Models;
using GuessMelody.Core.Storage;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;


namespace GuessMelody.Tests
{
    public partial class ButtonTestWindow : Window
    {
        private EspButtonHub _hub;
        private ButtonService _service;
        private DispatcherTimer _uiTimer;
        private DispatcherTimer _lampTimer;

        private ObservableCollection<ButtonSnapshot> _rows = new ObservableCollection<ButtonSnapshot>();

        private const string BindingsPath = "buttons.json";

        public ButtonTestWindow()
        {
            InitializeComponent();
            ButtonsGrid.ItemsSource = _rows;

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _uiTimer.Tick += (_, __) => RefreshGrid();
            _uiTimer.Start();

            _lampTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _lampTimer.Tick += (_, __) => { _lampTimer.Stop(); LampOff(); };

            Closed += (_, __) =>
            {
                _uiTimer.Stop();
                _lampTimer.Stop();
                StopServer();
            };

            Log("Готов. Нажмите «Старт», чтобы начать принимать пакеты.");
        }

        // =============================================================
        // Старт/стоп
        // =============================================================
        private void Start_Click(object sender, RoutedEventArgs e)
        {
            if (_hub != null) { Log("Уже запущено."); return; }

            if (!int.TryParse(PortBox.Text, out var port) || port <= 0 || port > 65535)
            {
                Log("Некорректный порт.");
                return;
            }

            try
            {
                _hub = new EspButtonHub(port, port);
                _service = new ButtonService(_hub);

                _service.PressReceived += OnPressReceived;
                _service.ButtonConnected += (s, b) => Log($"🟢 Подключилась: {b.Label} ({b.Mac})");
                _service.ButtonReconnected += (s, b) => Log($"🔗 Вернулась:    {b.Label} ({b.Mac})");
                _service.ButtonDisconnected += (s, b) => Log($"🔌 Потеряна:     {b.Label} ({b.Mac})");
                _service.RawLog += (s, m) => Log(m);

                _hub.Start();
                Log($"Сервер запущен на UDP:{port}");
            }
            catch (Exception ex)
            {
                Log("Ошибка старта: " + ex.Message);
                StopServer();
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e) => StopServer();

        private void StopServer()
        {
            try { _service?.Dispose(); } catch { }
            try { _hub?.Dispose(); } catch { }
            _service = null;
            _hub = null;
            Log("Сервер остановлен.");
        }

        // =============================================================
        // Привязки
        // =============================================================
        private void LoadBindings_Click(object sender, RoutedEventArgs e)
        {
            if (_service == null) { Log("Сначала запустите сервер."); return; }

            try
            {
                var bindings = JsonStore.Load<ButtonBindings>(BindingsPath);
                _service.ApplyBindings(bindings.Bindings);
                Log($"Загружено привязок: {bindings.Bindings.Count}");
                RefreshGrid(force: true);
            }
            catch (Exception ex) { Log("Ошибка загрузки привязок: " + ex.Message); }
        }

        private void SaveBindings_Click(object sender, RoutedEventArgs e)
        {
            if (_service == null) { Log("Сначала запустите сервер."); return; }

            try
            {
                var bindings = _service.ExportBindings();
                JsonStore.Save(BindingsPath, bindings);
                Log($"Сохранено привязок: {bindings.Bindings.Count} → {Path.GetFullPath(BindingsPath)}");
            }
            catch (Exception ex) { Log("Ошибка сохранения привязок: " + ex.Message); }
        }

        // =============================================================
        // Эмуляция нажатия
        // =============================================================
        private void Emulate_Click(object sender, RoutedEventArgs e)
        {
            if (_service == null) { Log("Сначала запустите сервер."); return; }
            _service.EmulatePress(EmuMacBox.Text.Trim());
        }

        // =============================================================
        // Нажатие
        // =============================================================
        private void OnPressReceived(object sender, PressEvent e)
        {
            // Вызывается из фонового потока → прыгаем в UI
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var snap = _service.GetByMac(e.Mac);
                var label = snap?.Label ?? e.Mac;

                Log($"🔴 PRESS: {label}  seq={e.Seq}  from={e.RemoteIp}");

                LampOn(label);
                RefreshGrid();
            }));
        }

        // =============================================================
        // Лампа
        // =============================================================
        public void LampOn(string text)
        {
            LampText.Text = text;
            LampText.Foreground = System.Windows.Media.Brushes.White;
            //(LampText.Parent as Border)!.Background =(System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FF3030")!;
            ((Border)LampText.Parent).Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3030"));
            _lampTimer.Stop();
            _lampTimer.Start();
        }

        public void LampOff()
        {
            LampText.Text = "— нет нажатий —";
            LampText.Foreground = System.Windows.Media.Brushes.Gray;
            //(LampText.Parent as Border)!.Background =(System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#EEE")!;
            ((Border)LampText.Parent).Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));

        }

        // =============================================================
        // Обновление таблицы
        // =============================================================
        private void RefreshGrid(bool force = false)
        {
            if (_service == null) return;

            var latest = _service.GetAll();

            // Простое обновление: чистим и заливаем заново.
            // Для 6 кнопок это дешевле и надёжнее, чем Diff.
            _rows.Clear();
            foreach (var b in latest) _rows.Add(b);
        }

        // =============================================================
        // Логи
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