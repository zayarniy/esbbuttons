using System.Windows;
using System.Windows.Input;
using GuessMelody.ViewModels;

namespace GuessMelody
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            PreviewKeyDown += OnPreviewKeyDown;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!(DataContext is MainViewModel vm)) return;

            // Не перехватываем, если фокус в текстовом поле — пользователь печатает
            if (Keyboard.FocusedElement is System.Windows.Controls.TextBox ||
                Keyboard.FocusedElement is System.Windows.Controls.PasswordBox)
                return;

            // 1..6 — эмуляция нажатия игрока
            if (e.Key >= Key.D1 && e.Key <= Key.D6)
            {
                int id = (int)(e.Key - Key.D0);
                vm.Game.TestPressCommand.Execute(id.ToString());
                e.Handled = true;
                return;
            }
            // NumPad 1..6
            if (e.Key >= Key.NumPad1 && e.Key <= Key.NumPad6)
            {
                int id = (int)(e.Key - Key.NumPad0);
                vm.Game.TestPressCommand.Execute(id.ToString());
                e.Handled = true;
                return;
            }

            // Q/W/E — начислить очко игроку 1/2/3
            if (e.Key == Key.Q) { vm.Game.TestScoreYesCommand.Execute(null); e.Handled = true; }
            if (e.Key == Key.W) { vm.Game.TestScoreYesCommand.Execute(null); e.Handled = true; }
            if (e.Key == Key.E) { vm.Game.TestScoreYesCommand.Execute(null); e.Handled = true; }

            // A — не начислять
            if (e.Key == Key.A) { vm.Game.TestScoreNoCommand.Execute(null); e.Handled = true; }

            // Space — старт/паника/следующий раунд (что уместно в текущем состоянии)
            if (e.Key == Key.Space)
            {
                vm.Game.QuickDemoCommand.Execute(null);
                e.Handled = true;
            }

            // Esc — panic
            if (e.Key == Key.Escape)
            {
                vm.Game.PanicCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}