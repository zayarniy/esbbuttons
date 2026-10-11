using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace EspButtonDiag.Wpf.ViewModels
{
    /// <summary>Инвертирует bool — для IsEnabled полей, пока сервер работает.</summary>
    public class BoolInverter : IValueConverter
    {
        public static readonly BoolInverter Instance = new BoolInverter();
        public object Convert(object v, Type t, object p, CultureInfo c) =>
            v is bool b ? !b : true;
        public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
            v is bool b ? !b : false;
    }

    /// <summary>Alive -> "●"/"○".</summary>
    public class AliveConverter : IValueConverter
    {
        public static readonly AliveConverter Instance = new AliveConverter();
        public object Convert(object v, Type t, object p, CultureInfo c) =>
            (v is bool b && b) ? "● жив" : "○ нет";
        public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
            throw new NotSupportedException();
    }

    /// <summary>LogKind -> Brush для раскраски журнала.</summary>
    public class KindToBrushConverter : IValueConverter
    {
        public static readonly KindToBrushConverter Instance = new KindToBrushConverter();
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            if (v is LogKind k)
            {
                switch (k)
                {
                    case LogKind.Connect: return Brushes.Green;
                    case LogKind.Reconnect: return Brushes.Teal;
                    case LogKind.Disconnect: return Brushes.OrangeRed;
                    case LogKind.Press: return Brushes.Red;
                    case LogKind.Error: return Brushes.DarkRed;
                    case LogKind.System: return Brushes.DarkSlateBlue;
                    case LogKind.Raw: return Brushes.Gray;
                }
            }
            return Brushes.Black;
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
            throw new NotSupportedException();
    }
}