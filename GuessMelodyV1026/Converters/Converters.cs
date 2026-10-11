using GuessMelody.Core.Enums;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace GuessMelody.Converters
{
    public class Converters : IValueConverter
    {
        public static readonly Converters Instance = new Converters();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is bool b ? !b : true;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is bool b ? !b : false;
    }

    public class LogKindToBrush : IValueConverter
    {
        public static readonly LogKindToBrush Instance = new LogKindToBrush();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is LogKind k)) return Brushes.Black;
            switch (k)
            {
                case LogKind.Raw: return Brushes.Gray;
                case LogKind.Connect: return Brushes.Green;
                case LogKind.Reconnect: return Brushes.Teal;
                case LogKind.Disconnect: return Brushes.OrangeRed;
                case LogKind.Press: return Brushes.Red;
                case LogKind.Hb: return Brushes.SteelBlue;
                case LogKind.System: return Brushes.DarkSlateBlue;
                case LogKind.Error: return Brushes.DarkRed;
                case LogKind.Game: return Brushes.MediumBlue;
                case LogKind.Audio: return Brushes.Purple;
                case LogKind.Score: return Brushes.DarkOrange;
                case LogKind.Http: return Brushes.DarkCyan;
                default: return Brushes.Black;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    public class RoundStateToText : IValueConverter
    {
        public static readonly RoundStateToText Instance = new RoundStateToText();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is RoundState s)) return "";
            switch (s)
            {
                case RoundState.Idle: return "Ожидание";
                case RoundState.Countdown: return "Отсчёт";
                case RoundState.Playing: return "Играет";
                case RoundState.WaitingForAnswer: return "Ждём ответ";
                case RoundState.Scored: return "Очко начислено";
                case RoundState.Finished: return "Завершено";
                default: return s.ToString();
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    public class AliveConverter : IValueConverter
    {
        public static readonly AliveConverter Instance = new AliveConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            (value is bool b && b) ? "● жив" : "○ нет";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    public class NullToVisibilityConverter : IValueConverter
    {
        public static readonly NullToVisibilityConverter Instance = new NullToVisibilityConverter();

        /// <summary>Если parameter == "Invert", логика инвертируется.</summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
            bool isNull = value == null;

            if (invert) isNull = !isNull;
            return isNull ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    public class TrackProgressConverter : IValueConverter
    {
        public static readonly TrackProgressConverter Instance = new TrackProgressConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is double d ? d : 0;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}