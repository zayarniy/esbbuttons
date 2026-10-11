using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GuessMelody.Wpf.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c) =>
            (v is bool b && b) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
            v is Visibility vis && vis == Visibility.Visible;
    }
}