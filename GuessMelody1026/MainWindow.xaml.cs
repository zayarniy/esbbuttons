using GuessMelody.Wpf.Game;
using GuessMelody.Wpf.Logging;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace GuessMelody.Wpf
{
    public partial class MainWindow : Window
    {
        private EmulatedButtonsService _emuButtons;
        public MainWindow()
        {
            InitializeComponent();

            SourceInitialized += (_, __) =>
            {
                // Создаём сервис, когда у окна гарантированно есть HWND.
                try
                {
                    _emuButtons = new EmulatedButtonsService(this);
                    Debug.WriteLine("[MainWindow] EmulatedButtonsService создан.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MainWindow] Emu init error: {ex}");
                }
            };

            Closed += (_, __) =>
            {
                try { _emuButtons?.Dispose(); } catch { }
                _emuButtons = null;
            };

            Loaded += (_, __) => UpdateStatus();
        }

        private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            ServerStatusText.Text = AppServices.IsServerRunning
                ? "Сервер: 🟢 запущен"
                : "Сервер: ⚪ остановлен";
        }

        /// <summary>Позволяет вкладкам попросить обновить статус-бар.</summary>
        public void RefreshServerStatus() => UpdateStatus();
    }
}