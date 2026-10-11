using System;
using System.Collections.Generic;
using System.Net;
using GuessMelody.Core;
using GuessMelody.Core.Enums;

namespace GuessMelody.Buttons
{
    public class ButtonService : IDisposable
    {
        private readonly EspButtonHub _hub;
        private readonly LogService _log;
        private readonly Dictionary<string, string> _macToName =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public ButtonsConfig Config { get; private set; } = new ButtonsConfig();
        public EspButtonHub Hub => _hub;

        // -------- События наружу --------
        public event EventHandler<PressEventArgs> Press;
        public event EventHandler<ButtonEventArgs> Connected;
        public event EventHandler<ButtonEventArgs> Reconnected;
        public event EventHandler<ButtonEventArgs> Disconnected;
        public event EventHandler<AckEventArgs> Ack;

        /// <summary>Каждый heartbeat от кнопки. Нужен для пульсации индикатора.</summary>
        public event EventHandler<ButtonEventArgs> Heartbeat;

        public ButtonService(LogService log,
                             int listenPort = 41234,
                             int ackPort = 41235,
                             int cmdPort = 41234)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _hub = new EspButtonHub(listenPort, ackPort, cmdPort);

            _hub.RawLog += (s, line) => _log.Add(LogKind.Raw, line);

            _hub.ButtonConnected += (s, e) =>
            {
                _log.Add(LogKind.Connect, $"+ {e.Button.Mac}");
                Connected?.Invoke(this, e);
            };
            _hub.ButtonReconnected += (s, e) =>
            {
                _log.Add(LogKind.Reconnect, $"+ {e.Button.Mac}");
                Reconnected?.Invoke(this, e);
            };
            _hub.ButtonDisconnected += (s, e) =>
            {
                _log.Add(LogKind.Disconnect, $"- {e.Button.Mac}");
                Disconnected?.Invoke(this, e);
            };
            _hub.PressReceived += (s, e) =>
            {
                _log.Add(LogKind.Press, $"PRESS {e.Button.Mac} seq={e.Seq}");
                Press?.Invoke(this, e);
            };
            _hub.CommandAck += (s, e) =>
            {
                _log.Add(LogKind.System, $"ACK {e.From}: {e.Payload}");
                Ack?.Invoke(this, e);
            };

            // ---- НОВОЕ: проброс heartbeat наружу ----
            _hub.HeartbeatReceived += (s, e) =>
            {
                // отдельно в лог не пишем — HB идут часто
                Heartbeat?.Invoke(this, e);
                _log.Add(LogKind.Hb, "SVC_HB: " + e.Button.Mac);   // ← временно
            };
        }

        public void Start() => _hub.Start();
        public void Stop() => _hub.Stop();

        public void ApplyBindings(ButtonsConfig cfg)
        {
            Config = cfg ?? new ButtonsConfig();
            _macToName.Clear();
            foreach (var b in Config.Bindings)
                if (!string.IsNullOrWhiteSpace(b.Mac))
                    _macToName[b.Mac] = b.TeamName;
            _log.Add(LogKind.System, $"Привязки кнопок применены ({Config.Bindings.Count})");
        }

        public string GetTeamName(string mac) =>
            _macToName.TryGetValue(mac, out var name) ? name : mac;

        public void SendSetServer(IPAddress ip, IEnumerable<string> macs = null) =>
            _hub.SendSetServer(ip, macs);

        public void SendSetWifi(string ssid, string password, IEnumerable<string> macs = null) =>
            _hub.SendSetWifi(ssid, password, macs);

        public void SendReboot(IEnumerable<string> macs = null) =>
            _hub.SendReboot(macs);

        public void Dispose() => _hub?.Dispose();
    }
}