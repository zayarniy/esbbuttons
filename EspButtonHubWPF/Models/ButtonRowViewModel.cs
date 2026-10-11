using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EspButtonDiag.Wpf.Models
{
    public class ButtonRowViewModel : INotifyPropertyChanged
    {
        public string Mac { get; }
        public string FirstSeenLocal => _firstSeen.ToLocalTime().ToString("HH:mm:ss");
        public string LastSeenLocal => _lastSeen.ToLocalTime().ToString("HH:mm:ss");
        public string LastIp { get; private set; }
        public bool Alive { get; private set; }
        public int PressCount { get; private set; }
        public string LastSeq { get; private set; }
        public long LastUptimeMs { get; private set; }

        // ==== НОВОЕ: heartbeat ====
        public uint HbReceived { get; private set; }
        public uint HbExpected { get; private set; }
        public double DeliveryPercent { get; private set; }
        public string DeliveryText { get; private set; }
        public bool Rebooting { get; private set; }

        public string SecondsSinceLastSeen =>
            (DateTime.UtcNow - _lastSeen).TotalSeconds.ToString("F1");

        private DateTime _firstSeen;
        private DateTime _lastSeen;

        public ButtonRowViewModel(string mac)
        {
            Mac = mac;
            _firstSeen = DateTime.UtcNow;
            _lastSeen = DateTime.UtcNow;
            Alive = true;
            LastIp = ""; LastSeq = "";
            DeliveryPercent = 100.0;
            DeliveryText = "100%";
        }

        public void Update(ButtonInfo info)
        {
            _firstSeen = info.FirstSeenUtc;
            _lastSeen = info.LastSeenUtc;
            LastIp = info.LastIp?.ToString() ?? "";
            Alive = info.Alive;
            PressCount = info.PressCount;
            LastSeq = info.LastSeq ?? "";
            LastUptimeMs = info.LastUptimeMs;

            HbReceived = info.ReceivedHbCount;
            HbExpected = info.ExpectedHbCount;
            DeliveryPercent = info.DeliveryPercent;
            DeliveryText = $"{info.DeliveryPercent:F1}%  ({HbReceived}/{HbExpected})";
            Rebooting = info.Rebooting;

            OnPropertyChanged(nameof(FirstSeenLocal));
            OnPropertyChanged(nameof(LastSeenLocal));
            OnPropertyChanged(nameof(LastIp));
            OnPropertyChanged(nameof(Alive));
            OnPropertyChanged(nameof(PressCount));
            OnPropertyChanged(nameof(LastSeq));
            OnPropertyChanged(nameof(LastUptimeMs));
            OnPropertyChanged(nameof(SecondsSinceLastSeen));
            OnPropertyChanged(nameof(HbReceived));
            OnPropertyChanged(nameof(HbExpected));
            OnPropertyChanged(nameof(DeliveryPercent));
            OnPropertyChanged(nameof(DeliveryText));
            OnPropertyChanged(nameof(Rebooting));
        }

        public void RefreshTime() => OnPropertyChanged(nameof(SecondsSinceLastSeen));

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string p = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}