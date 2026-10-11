using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using EspButtonDiag.Wpf.ViewModels;

namespace EspButtonDiag.Wpf
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            if (DataContext is MainViewModel vm)
            {
                ((INotifyCollectionChanged)vm.Log).CollectionChanged += (_, __) =>
                {
                    if (LogList.Items.Count > 0)
                        LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
                };
            }
        }
    }
}