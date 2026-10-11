using System;
using GuessMelody.Buttons;

namespace GuessMelody.ViewModels
{
    public class ButtonRowViewModel : ViewModelBase
    {

        private string _hbSecondsAgo = "—";
        public string HbSecondsAgo
        {
            get => _hbSecondsAgo;
            private set => Set(ref _hbSecondsAgo, value);
        }

        private DateTime _lastHbUtc;


        public string Mac { get; }

        private string _name = "";
        public string Name
        {
            get => _name;
            set => Set(ref _name, value);
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }

        private string _lastIp = "";
        public string LastIp
        {
            get => _lastIp;
            private set => Set(ref _lastIp, value);
        }

        private bool _alive = true;
        public bool Alive
        {
            get => _alive;
            private set => Set(ref _alive, value);
        }

        private int _pressCount;
        public int PressCount
        {
            get => _pressCount;
            private set => Set(ref _pressCount, value);
        }

        private string _lastSeq = "";
        public string LastSeq
        {
            get => _lastSeq;
            private set => Set(ref _lastSeq, value);
        }

        private long _lastUptimeMs;
        public long LastUptimeMs
        {
            get => _lastUptimeMs;
            private set => Set(ref _lastUptimeMs, value);
        }

        private string _deliveryText = "100%";
        public string DeliveryText
        {
            get => _deliveryText;
            private set => Set(ref _deliveryText, value);
        }

        private double _deliveryPercent = 100.0;
        public double DeliveryPercent
        {
            get => _deliveryPercent;
            private set => Set(ref _deliveryPercent, value);
        }

        private bool _rebooting;
        public bool Rebooting
        {
            get => _rebooting;
            private set => Set(ref _rebooting, value);
        }

        private string _firstSeenLocal = "";
        public string FirstSeenLocal
        {
            get => _firstSeenLocal;
            private set => Set(ref _firstSeenLocal, value);
        }

        private string _lastSeenLocal = "";
        public string LastSeenLocal
        {
            get => _lastSeenLocal;
            private set => Set(ref _lastSeenLocal, value);
        }

        private string _secondsSinceLastSeen = "0.0";
        public string SecondsSinceLastSeen
        {
            get => _secondsSinceLastSeen;
            private set => Set(ref _secondsSinceLastSeen, value);
        }

        private DateTime _lastSeenUtc;

        public ButtonRowViewModel(string mac, string name = "")
        {
            Mac = mac;
            _name = name;
            _lastSeenUtc = DateTime.UtcNow;
        }

        public void Update(ButtonInfo info)
        {
            if (info == null) return;

            LastIp = info.LastIp?.ToString() ?? "";
            Alive = info.Alive;
            PressCount = info.PressCount;
            LastSeq = info.LastSeq ?? "";
            LastUptimeMs = info.LastUptimeMs;
            DeliveryPercent = info.DeliveryPercent;
            DeliveryText = $"{info.DeliveryPercent:F1}%  ({info.ReceivedHbCount}/{info.ExpectedHbCount})";
            Rebooting = info.Rebooting;
            FirstSeenLocal = info.FirstSeenUtc.ToLocalTime().ToString("HH:mm:ss");
            LastSeenLocal = info.LastSeenUtc.ToLocalTime().ToString("HH:mm:ss");
            _lastSeenUtc = info.LastSeenUtc;
            _lastHbUtc = info.LastSeenUtc;   // последний HB = последний приход пакета от кнопки
            RefreshTime();
            RefreshHbSecondsAgo();
        }

        public void RefreshTime() =>
            SecondsSinceLastSeen = (DateTime.UtcNow - _lastSeenUtc).TotalSeconds.ToString("F1");

        /// <summary>Сколько секунд назад приходил heartbeat.</summary>
        public void RefreshHbSecondsAgo()
        {
            // Если HB ни разу не было — показываем прочерк
            if (_lastHbUtc == default(DateTime))
            {
                HbSecondsAgo = "—";
                return;
            }

            var sec = (DateTime.UtcNow - _lastHbUtc).TotalSeconds;
            HbSecondsAgo = sec < 0 ? "0.0" : sec.ToString("F1");
        }

    }
}