using System;
using System.Collections.ObjectModel;
using System.Windows;
using GuessMelody.Core.Enums;

namespace GuessMelody.Core
{
    public class LogService
    {
        private const int MaxEntries = 50_000;

        public ObservableCollection<LogEntry> Entries { get; } = new ObservableCollection<LogEntry>();

        public event EventHandler<LogEntry> EntryAdded;

        public void Add(LogKind kind, string message)
        {
            var e = new LogEntry(DateTime.Now, kind, message);

            var app = Application.Current;
            if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
                app.Dispatcher.BeginInvoke(new Action(() => AppendInternal(e)));
            else
                AppendInternal(e);
        }

        private void AppendInternal(LogEntry e)
        {
            Entries.Add(e);
            while (Entries.Count > MaxEntries) Entries.RemoveAt(0);
            EntryAdded?.Invoke(this, e);
        }

        public void Clear()
        {
            var app = Application.Current;
            if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
                app.Dispatcher.BeginInvoke(new Action(() => Entries.Clear()));
            else
                Entries.Clear();
        }
    }
}