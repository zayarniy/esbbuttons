using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using GuessMelody.Wpf.Logging;

namespace GuessMelody.Wpf.Converters
{
    public class LevelToBrushConverter : IValueConverter
    {
        public static readonly LevelToBrushConverter Instance = new LevelToBrushConverter();

        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            if (v is LogLevel lvl)
            {
                switch (lvl)
                {
                    case LogLevel.Raw: return Brushes.Gray;
                    case LogLevel.Info: return Brushes.SteelBlue;
                    case LogLevel.State: return Brushes.DarkViolet;
                    case LogLevel.Press: return Brushes.Red;
                    case LogLevel.Score: return Brushes.ForestGreen;
                    case LogLevel.Error: return Brushes.DarkRed;
                }
            }
            return Brushes.Black;
        }

        public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
            throw new NotSupportedException();
    }
}