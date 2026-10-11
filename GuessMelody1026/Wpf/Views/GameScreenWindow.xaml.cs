using GuessMelody.Wpf.Game;
using GuessMelody.Wpf.ViewModels;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GuessMelody.Wpf.Views
{
    public partial class GameScreenWindow : Window
    {
        public GameScreenViewModel ViewModel { get; }

        public event EventHandler<int> CategoryChosenFromUi;

        public GameScreenWindow(GameScreenViewModel vm)
        {
            InitializeComponent();
            ViewModel = vm;
            DataContext = vm;
            vm.CategoryChosen += (s, idx) => CategoryChosenFromUi?.Invoke(this, idx);
        }

        // -------------------------------------------------------------
        // Клик по категории
        // -------------------------------------------------------------
        private void Category_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is int index1based)
            {
                ViewModel.SelectCategory(index1based - 1);
            }
        }



        // -------------------------------------------------------------
        // Клавиши 1–9
        // -------------------------------------------------------------
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            int idx = -1;
            if (e.Key >= Key.D1 && e.Key <= Key.D9) idx = e.Key - Key.D1;
            else if (e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9) idx = e.Key - Key.NumPad1;

            if (idx >= 0)
            {
                ViewModel.SelectCategory(idx);
                e.Handled = true;
            }
        }

        private void ScoreTile_LeftClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string mac)
            {
                GameController.Instance.AddScore(mac, +1);
                e.Handled = true;
            }
        }

        private void ScoreTile_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string mac)
            {
                GameController.Instance.AddScore(mac, -1);
                e.Handled = true;
            }
        }
    }
}