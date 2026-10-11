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
        public PlayerWindow()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                if (DataContext is PlayerWindowViewModel vm)
                    vm.RebuildFromSettings();
            };
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
