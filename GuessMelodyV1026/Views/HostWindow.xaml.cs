using System.Windows;
using System.Windows.Input;
using GuessMelody.ViewModels;

namespace GuessMelody.Views
{
    public partial class HostWindow : Window
    {
        public HostWindow()
        {
            InitializeComponent();
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
    }
}