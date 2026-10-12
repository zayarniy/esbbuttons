using GuessMelody.ViewModels;
using System;
using System.Windows;
using System.Windows.Input;

namespace GuessMelody.Views
{
    public partial class HostWindow : Window
    {

        public static HostWindow Current { get; private set; }

        public HostWindow()
        {
            InitializeComponent();

            Current = this;
            //Closed += (s, e) => { if (Current == this) Current = null; };
            Closed += (s, e) =>
            {
                if (ReferenceEquals(Current, this)) Current = null;
                (DataContext as IDisposable)?.Dispose();
            };
            PreviewKeyDown += OnPreviewKeyDown;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!(DataContext is HostWindowViewModel vm)) return;

            // Space — старт раунда / следующего
            if (e.Key == Key.Space)
            {
                vm.StartRoundCommand.Execute(null);
                e.Handled = true;
                return;
            }

            // Enter — Да
            if (e.Key == Key.Enter)
            {
                vm.YesCommand.Execute(null);
                e.Handled = true;
                return;
            }

            // Backspace — Нет
            if (e.Key == Key.Back)
            {
                vm.NoCommand.Execute(null);
                e.Handled = true;
                return;
            }

            // P — пауза
            if (e.Key == Key.P)
            {
                vm.PauseCommand.Execute(null);
                e.Handled = true;
                return;
            }

            // R — продолжить
            if (e.Key == Key.R)
            {
                vm.ResumeCommand.Execute(null);
                e.Handled = true;
                return;
            }

            // Esc — Panic
            if (e.Key == Key.Escape)
            {
                vm.PanicCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        public static void ShowOrActivate()
        {
            if (Current != null && Current.IsLoaded)
            {
                if (Current.WindowState == WindowState.Minimized)
                    Current.WindowState = WindowState.Normal;

                Current.Activate();
                Current.Topmost = true;
                Current.Topmost = false;
                Current.Focus();
                return;
            }

            var vm = App.Services.GetService(typeof(HostWindowViewModel)) as HostWindowViewModel;
            if (vm == null)
            {
                MessageBox.Show("Не удалось создать модель окна ведущего.",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var win = new HostWindow
            {
                DataContext = vm,
                Owner = Application.Current?.MainWindow
            };
            Current = win;
            win.Show();
        }
    }
}
