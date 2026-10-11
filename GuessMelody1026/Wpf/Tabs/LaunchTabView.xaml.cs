using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GuessMelody.Core.Models;
using GuessMelody.Wpf.Game;
using GuessMelody.Wpf.Logging;
using GuessMelody.Wpf.ViewModels;
using GuessMelody.Wpf.Views;

namespace GuessMelody.Wpf.Tabs
{
    public partial class LaunchTabView : UserControl
    {
        private GameScreenWindow _screenWindow;
        private GameScreenWindow _mirrorWindow;
        private HostConsoleWindow _hostWindow;

        private GameScreenViewModel _screenVm;
        private HostConsoleViewModel _hostVm;

        private DispatcherTimer _statusTimer;

        public LaunchTabView()
        {
            InitializeComponent();

            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statusTimer.Tick += (_, __) => UpdateStatuses();
            Loaded += (_, __) => { _statusTimer.Start(); UpdateStatuses(); };
            Unloaded += (_, __) => _statusTimer.Stop();
        }

        // =============================================================
        // Сервер
        // =============================================================
        private void StartServer_Click(object sender, RoutedEventArgs e)
        {
            if (AppServices.IsServerRunning) { Log("Сервер уже запущен."); return; }

            if (!int.TryParse(PortBox.Text, out var port) || port <= 0 || port > 65535)
            {
                Log("Некорректный порт."); return;
            }

            try
            {
                AppServices.StartServer(port);
                GameController.Instance.AttachButtons();
                Log($"UDP сервер запущен на порту {port}.");
                (Window.GetWindow(this) as MainWindow)?.RefreshServerStatus();
            }
            catch (Exception ex)
            {
                Log("Ошибка запуска сервера: " + ex.Message);
                AppServices.StopServer();
            }
        }

        private void StopServer_Click(object sender, RoutedEventArgs e)
        {
            CloseAllWindowsInternal();
            AppServices.StopServer();
            Log("UDP сервер остановлен.");
            (Window.GetWindow(this) as MainWindow)?.RefreshServerStatus();
        }

        // =============================================================
        // Экран игроков
        // =============================================================
        private void OpenScreen_Click(object sender, RoutedEventArgs e)
        {
            if (_screenWindow != null) { _screenWindow.Activate(); return; }

            if (!EnsureControllerReady()) return;

            var gc = GameController.Instance;
            _screenVm = new GameScreenViewModel(gc.Engine);

            _screenVm.LoadCategories(AppServices.FolderManager.Categories);
            RefreshScores();

            _screenWindow = new GameScreenWindow(_screenVm)
            {
                Title = "Угадай мелодию — экран игроков"
            };
            _screenWindow.CategoryChosenFromUi += OnCategoryChosen;
            _screenWindow.Closed += (_, __) => { _screenWindow = null; UpdateStatuses(); };

            _screenWindow.Show();
            Log("Экран игроков открыт.");
            UpdateStatuses();
        }

        private void CloseScreen_Click(object sender, RoutedEventArgs e)
        {
            _screenWindow?.Close();
            _screenWindow = null;
            UpdateStatuses();
        }

        private void FullscreenScreen_Click(object sender, RoutedEventArgs e)
        {
            if (_screenWindow == null)
            {
                Log("Экран игроков ещё не открыт.");
                return;
            }

            if (_screenWindow.WindowStyle == WindowStyle.None &&
                _screenWindow.WindowState == WindowState.Maximized)
            {
                _screenWindow.WindowStyle = WindowStyle.SingleBorderWindow;
                _screenWindow.WindowState = WindowState.Normal;
            }
            else
            {
                _screenWindow.WindowStyle = WindowStyle.None;
                _screenWindow.WindowState = WindowState.Maximized;
            }
        }

        // =============================================================
        // Дубль для помощника
        // =============================================================
        private void OpenMirror_Click(object sender, RoutedEventArgs e)
        {
            if (_mirrorWindow != null) { _mirrorWindow.Activate(); return; }
            if (_screenVm == null)
            {
                Log("Сначала откройте экран игроков — дубль использует ту же VM.");
                return;
            }

            _mirrorWindow = new GameScreenWindow(_screenVm)
            {
                Title = "Угадай мелодию — экран для помощника"
            };
            _mirrorWindow.Closed += (_, __) => { _mirrorWindow = null; UpdateStatuses(); };
            _mirrorWindow.Show();
            Log("Дубль для помощника открыт.");
            UpdateStatuses();
        }

        private void CloseMirror_Click(object sender, RoutedEventArgs e)
        {
            _mirrorWindow?.Close();
            _mirrorWindow = null;
            UpdateStatuses();
        }

        // =============================================================
        // Пульт ведущего
        // =============================================================
        private void OpenHost_Click(object sender, RoutedEventArgs e)
        {
            if (_hostWindow != null) { _hostWindow.Activate(); return; }
            if (!EnsureControllerReady()) return;

            _hostVm = new HostConsoleViewModel(AppServices.Audio);
            _hostWindow = new HostConsoleWindow(_hostVm);
            _hostWindow.Closed += (_, __) => { _hostWindow = null; UpdateStatuses(); };
            _hostWindow.Show();
            Log("Пульт ведущего открыт.");
            UpdateStatuses();
        }

        private void CloseHost_Click(object sender, RoutedEventArgs e)
        {
            _hostWindow?.Close();
            _hostWindow = null;
            UpdateStatuses();
        }

        // =============================================================
        // Быстрое управление раундом
        // =============================================================
        private void StartCat1_Click(object sender, RoutedEventArgs e) => StartRoundSafe(0);
        private void StartCat2_Click(object sender, RoutedEventArgs e) => StartRoundSafe(1);
        private void StartCat3_Click(object sender, RoutedEventArgs e) => StartRoundSafe(2);
        private void StartCat4_Click(object sender, RoutedEventArgs e) => StartRoundSafe(3);
        private void StartCat5_Click(object sender, RoutedEventArgs e) => StartRoundSafe(4);
        private void StartCat6_Click(object sender, RoutedEventArgs e) => StartRoundSafe(5);
        private void StartCat7_Click(object sender, RoutedEventArgs e) => StartRoundSafe(6);
        private void StartCat8_Click(object sender, RoutedEventArgs e) => StartRoundSafe(7);
        private void StartCat9_Click(object sender, RoutedEventArgs e) => StartRoundSafe(8);

        private void StartRoundSafe(int index)
        {
            if (!EnsureControllerReady()) return;
            if (index < 0 || index >= AppServices.FolderManager.Categories.Count)
            {
                Log($"Категория #{index + 1} не существует.");
                return;
            }
            GameController.Instance.StartRound(index);
            Log($"Старт раунда, категория #{index + 1}.");
        }

        private void HostYes_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureControllerReady()) return;
            GameController.Instance.HostSaysYes(2);
            Log("Ведущий: Да (+2).");
        }

        private void HostNo_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureControllerReady()) return;
            GameController.Instance.HostSaysNo();
            Log("Ведущий: Нет.");
        }

        private void HostNoOne_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureControllerReady()) return;
            GameController.Instance.HostSaysNoOne();
            Log("Ведущий: Никто не ответил.");
        }

        private void ResetScores_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Сбросить очки всех игроков?",
                    "Подтверждение", MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            GameController.Instance.ResetScores();
            RefreshScores();
            Log("Очки сброшены.");
        }

        private void ResetGame_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Сбросить игру (очки, история, сыгранные треки)?",
                    "Подтверждение", MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            GameController.Instance.ResetGame();
            RefreshScores();
            Log("Игра сброшена.");
        }

        // =============================================================
        // Внутреннее
        // =============================================================
        private bool EnsureControllerReady()
        {
            if (!AppServices.IsServerRunning)
            {
                Log("Сначала запустите UDP-сервер (кнопка сверху).");
                return false;
            }
            if (AppServices.FolderManager == null ||
                AppServices.FolderManager.Categories.Count == 0)
            {
                Log("Сначала выберите папку с музыкой на вкладке «Папки».");
                return false;
            }
            return true;
        }

        private void OnCategoryChosen(object sender, int index)
        {
            // Пользователь кликнул плитку (или нажал 1–9) в окне игрока.
            var gc = GameController.Instance;
            if (gc.Engine == null) return;

            // Игнорируем, если раунд уже идёт.
            if (gc.Engine.State != GuessMelody.Core.Models.RoundState.Idle) return;

            gc.StartRound(index);
            Log($"Категория #{index + 1} выбрана с экрана игроков.");
        }

        private void RefreshScores()
        {
            if (_screenVm == null) return;

            var players = AppServices.ButtonService?.GetAll()
                          ?? new System.Collections.Generic.List<ButtonSnapshot>();
            _screenVm.UpdateScores(players, mac => GameController.Instance.GetScore(mac));
        }

        private void CloseAllWindowsInternal()
        {
            try { _screenWindow?.Close(); } catch { }
            try { _mirrorWindow?.Close(); } catch { }
            try { _hostWindow?.Close(); } catch { }
            _screenWindow = null;
            _mirrorWindow = null;
            _hostWindow = null;
        }

        private void UpdateStatuses()
        {
            ServerStatusText.Text = AppServices.IsServerRunning
                ? "🟢 запущен" : "⚪ остановлен";

            ScreenStatus.Text = _screenWindow != null ? "открыт" : "закрыт";
            ScreenStatus.Foreground = _screenWindow != null
                ? System.Windows.Media.Brushes.Green
                : System.Windows.Media.Brushes.Gray;

            MirrorStatus.Text = _mirrorWindow != null ? "открыт" : "закрыт";
            MirrorStatus.Foreground = _mirrorWindow != null
                ? System.Windows.Media.Brushes.Green
                : System.Windows.Media.Brushes.Gray;

            HostStatus.Text = _hostWindow != null ? "открыт" : "закрыт";
            HostStatus.Foreground = _hostWindow != null
                ? System.Windows.Media.Brushes.Green
                : System.Windows.Media.Brushes.Gray;
        }

        private void Log(string s)
        {
            AppLogger.Instance.Info(s);
            LogList.Items.Add($"[{DateTime.Now:HH:mm:ss.fff}] {s}");
            if (LogList.Items.Count > 500) LogList.Items.RemoveAt(0);
            LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
        }
    }
}