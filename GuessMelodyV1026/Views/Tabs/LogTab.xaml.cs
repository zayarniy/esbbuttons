using System.Collections.Specialized;
using System.Windows.Controls;
using GuessMelody.ViewModels;

namespace GuessMelody.Views.Tabs
{
    public partial class LogTab : UserControl
    {
        public LogTab()
        {
            InitializeComponent();

            DataContextChanged += (s, e) =>
            {
                if (DataContext is LogTabViewModel vm)
                {
                    //((INotifyCollectionChanged)vm.View).CollectionChanged += (_, __) =>
                    //{
                    //    if (vm.AutoScroll && LogList.Items.Count > 0)
                    //        LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
                    //};
                }
            };
        }
    }
}