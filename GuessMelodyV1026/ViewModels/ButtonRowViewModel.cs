using System;
using GuessMelody.Buttons;

namespace GuessMelody.ViewModels
{
    public class ButtonRowViewModel : ViewModelBase
    {
        public int Id { get; }

        private string _name = "";
        public string Name
        {
            get => _name;
            set => Set(ref _name, value);
        }

        private string _mac = "";
        public string Mac
        {
            get => _mac;
            set => Set(ref _mac, value);
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

        private bool _alive;
        public bool Alive
        {
            get => _alive;
            private set
            {
                if (Set(ref _alive, value))
                    OnPropertyChanged(nameof(IsAliveText));
            }
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

        private string _deliveryText = "—";
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

        private string _firstSeenLocal = "—";
        public string FirstSeenLocal
        {
            get => _firstSeenLocal;
            private set => Set(ref _firstSeenLocal, value);
        }

        private string _lastSeenLocal = "—";
        public string LastSeenLocal
        {
            get => _lastSeenLocal;
            private set => Set(ref _lastSeenLocal, value);
        }

        private string _secondsSinceLastSeen = "—";
        public string SecondsSinceLastSeen
        {
            get => _secondsSinceLastSeen;
            private set => Set(ref _secondsSinceLastSeen, value);
        }

        // «Пустой слот» — MAC = VIRT:NN или пусто. Захват/подключение возможно.
        public bool IsVirtual =>
            string.IsNullOrWhiteSpace(Mac) ||
            Mac.StartsWith("VIRT:", StringComparison.OrdinalIgnoreCase);

        public string IsAliveText => Alive ? "● жив" : "○ нет";

        private DateTime _lastSeenUtc;
        private bool _hasHb;

        public ButtonRowViewModel(int id, string name, string mac)
        {
            Id = id;
            _name = name;
            _mac = mac;
            _lastSeenUtc = DateTime.UtcNow;
        }

        /// <summary>Данные от UDP-хаба (IP, жив, нажатий, HB).</summary>
        public void UpdateHub(ButtonInfo info)
        {
            if (info == null) return;

            LastIp = info.LastIp?.ToString() ?? "";
            Alive = info.Alive;
            PressCount = info.PressCount;
            LastSeq = info.LastSeq ?? "";
            LastUptimeMs = info.LastUptimeMs;
            DeliveryPercent = info.DeliveryPercent;
            DeliveryText = $"{info.DeliveryPercent:F0}%  ({info.ReceivedHbCount}/{info.ExpectedHbCount})";
            Rebooting = info.Rebooting;
            FirstSeenLocal = info.FirstSeenUtc.ToLocalTime().ToString("HH:mm:ss");
            LastSeenLocal = info.LastSeenUtc.ToLocalTime().ToString("HH:mm:ss");
            _lastSeenUtc = info.LastSeenUtc;
            _hasHb = true;
            RefreshTime();
        }

        public void RefreshTime()
        {
            SecondsSinceLastSeen = _hasHb
                ? (DateTime.UtcNow - _lastSeenUtc).TotalSeconds.ToString("F1")
                : "—";
            OnPropertyChanged(nameof(IsVirtual));
        }
    }
}