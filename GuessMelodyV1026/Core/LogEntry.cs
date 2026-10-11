using System;
using GuessMelody.Core.Enums;

namespace GuessMelody.Core
{
    public class LogEntry
    {
        public DateTime Time { get; }
        public LogKind Kind { get; }
        public string Message { get; }

        public string TimeText => Time.ToString("HH:mm:ss.fff");
        public string KindText => Kind.ToString();

        public LogEntry(DateTime t, LogKind k, string m)
        {
            Time = t;
            Kind = k;
            Message = m;
        }
    }
}