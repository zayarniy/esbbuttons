using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using GuessMelody.ViewModels;

namespace GuessMelody.Views.Tabs
{
    public partial class GameTab : UserControl
    {
        public GameTab()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is GameTabViewModel oldVm)
            {
                oldVm.OpenPlayer -= OnOpenPlayer;
                oldVm.OpenHost -= OnOpenHost;
            }
            if (e.NewValue is GameTabViewModel newVm)
            {
                newVm.OpenPlayer += OnOpenPlayer;
                newVm.OpenHost += OnOpenHost;
            }
        }

        private void OnOpenPlayer(object sender, EventArgs e)
        {
            var vm = App.Services.GetRequiredService<PlayerWindowViewModel>();
            var win = new PlayerWindow { DataContext = vm };
            win.Owner = Window.GetWindow(this);
            win.Show();
        }

        private void OnOpenHost(object sender, EventArgs e)
        {
            var vm = App.Services.GetRequiredService<HostWindowViewModel>();
            var win = new HostWindow { DataContext = vm };
            win.Owner = Window.GetWindow(this);
            win.Show();
        }
    }
}