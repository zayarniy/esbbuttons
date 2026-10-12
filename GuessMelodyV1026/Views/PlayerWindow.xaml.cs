using GuessMelody.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace GuessMelody.Views
{
    /// <summary>
    /// Interaction logic for PlayerWindow.xaml
    /// </summary>
    public partial class PlayerWindow : Window
    {
        public static PlayerWindow Current { get; private set; }

        public PlayerWindow()
        {
            InitializeComponent();

            Current = this;
            Closed += (s, e) => { if (Current == this) Current = null; };


            DataContextChanged += (s, e) =>
            {
                if (DataContext is PlayerWindowViewModel vm)
                    vm.RebuildFromSettings();
            };
        }

        /// <summary>
        /// Открыть окно игроков. Если уже открыто — активировать.
        /// Создаёт VM через DI, если окно нужно открыть впервые.
        /// </summary>
        public static void ShowOrActivate()
        {
            // Уже открыто — просто на передний план
            if (Current != null && Current.IsLoaded)
            {
                if (Current.WindowState == WindowState.Minimized)
                    Current.WindowState = WindowState.Normal;

                Current.Activate();
                Current.Topmost = true;
                Current.Topmost = false;   // кратко — на верх, потом отпускаем
                Current.Focus();
                return;
            }

            // Создаём новое окно с VM из DI
            var vm = App.Services.GetService(typeof(PlayerWindowViewModel)) as PlayerWindowViewModel;
            if (vm == null)
            {
                MessageBox.Show("Не удалось создать модель окна игроков.",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var win = new PlayerWindow
            {
                DataContext = vm,
                Owner = Application.Current?.MainWindow
            };
            Current = win;
            win.Show();
        }
    
        private void TeamTile_LeftClick(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is FrameworkElement fe)) return;
            var name = fe.Tag as string;
            if (string.IsNullOrEmpty(name)) return;
            var vm = DataContext as PlayerWindowViewModel;
            vm?.FlashCommand.Execute(name);
        }

        private void TeamTile_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is FrameworkElement fe)) return;
            var name = fe.Tag as string;
            if (string.IsNullOrEmpty(name)) return;
            var vm = DataContext as PlayerWindowViewModel;
            vm?.UnflashCommand.Execute(name);
        }
    }
}
