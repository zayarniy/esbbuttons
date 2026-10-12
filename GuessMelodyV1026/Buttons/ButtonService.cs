using GuessMelody.Core;
using GuessMelody.Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace GuessMelody.Buttons
{
    public class ButtonService : IDisposable
    {
        private readonly EspButtonHub _hub;
        private readonly LogService _log;
        private readonly GameEngine _engine;

        // Быстрый индекс MAC → имя команды (для логов и подсказок).
        private readonly Dictionary<string, string> _macToName =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public EspButtonHub Hub => _hub;

        public event EventHandler<PressEventArgs> Press;
        public event EventHandler<ButtonEventArgs> Connected;
        public event EventHandler<ButtonEventArgs> Reconnected;
        public event EventHandler<ButtonEventArgs> Disconnected;
        public event EventHandler<ButtonEventArgs> Heartbeat;
        public event EventHandler<AckEventArgs> Ack;

        public ButtonService(LogService log,
                             GameEngine engine,
                             int listenPort = 41234,
                             int ackPort = 41235,
                             int cmdPort = 41234)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));

            _hub = new EspButtonHub(listenPort, ackPort, cmdPort);

            // ---- Подписки на UDP-события ----
            _hub.RawLog += (s, line) => _log.Add(LogKind.Raw, line);

            _hub.ButtonConnected += (s, e) =>
            {
                _log.Add(LogKind.Connect, $"+ {e.Button.Mac} ({GetTeamNameOrMac(e.Button.Mac)})");
                Connected?.Invoke(this, e);
            };

            _hub.ButtonReconnected += (s, e) =>
            {
                _log.Add(LogKind.Reconnect, $"+ {e.Button.Mac} ({GetTeamNameOrMac(e.Button.Mac)})");
                Reconnected?.Invoke(this, e);
            };

            _hub.ButtonDisconnected += (s, e) =>
            {
                _log.Add(LogKind.Disconnect, $"- {e.Button.Mac} ({GetTeamNameOrMac(e.Button.Mac)})");
                Disconnected?.Invoke(this, e);
            };

            _hub.HeartbeatReceived += (s, e) =>
            {
                // Движок сам разберётся: изменит IsAlive, выстрелит MacSeen/Heartbeat
                _engine.OnHeartbeat(e.Button.Mac);
                Heartbeat?.Invoke(this, e);
            };

            _hub.PressReceived += (s, e) =>
            {
                // Сначала сообщим движку «мы видели этот MAC» (для захвата),
                // потом отдадим сам нажим — реальное нажатие обрабатывает PressBridge.
                _engine.NotifyUnknownMac(e.Button.Mac);

                _log.Add(LogKind.Press,
                    $"PRESS {GetTeamNameOrMac(e.Button.Mac)} ({e.Button.Mac}) seq={e.Seq}");

                Press?.Invoke(this, e);
            };

            _hub.CommandAck += (s, e) =>
            {
                _log.Add(LogKind.System, $"ACK {e.From}: {e.Payload}");
                Ack?.Invoke(this, e);
            };

            // ---- Подписка на изменения состава команд ----
            _engine.TeamsChanged += (s, e) => RebuildIndex();
            RebuildIndex();
        }

        // ---------------------------------------------------------
        // Публичный API
        // ---------------------------------------------------------
        public void Start() => _hub.Start();
        public void Stop() => _hub.Stop();

        /// <summary>Найти имя команды по MAC или вернуть сам MAC, если не найдено.</summary>
        public string GetTeamNameOrMac(string mac)
        {
            if (string.IsNullOrWhiteSpace(mac)) return "";
            return _macToName.TryGetValue(mac, out var name) ? name : mac;
        }

        /// <summary>Устаревшее — оставлено для совместимости.
        /// Привязки теперь живут в GameEngine.Teams.</summary>
        [Obsolete("Привязки MAC↔имя теперь в GameSettings.Buttons.Teams. Используйте GameEngine.AssignMac/RenameTeam.")]
        public void ApplyBindings(ButtonsConfig cfg)
        {
            if (cfg?.Bindings == null || cfg.Bindings.Count == 0) return;

            foreach (var b in cfg.Bindings)
            {
                var team = _engine.FindTeamByMac(b.Mac)
                        ?? _engine.Teams.FirstOrDefault(t => t.Id == b.TeamId);
                if (team == null) continue;

                if (!string.IsNullOrWhiteSpace(b.Mac) &&
                    !string.Equals(team.Mac, b.Mac, StringComparison.OrdinalIgnoreCase))
                {
                    _engine.AssignMac(team.Id, b.Mac, force: false);
                }
                if (!string.IsNullOrWhiteSpace(b.TeamName))
                {
                    _engine.RenameTeam(team.Id, b.TeamName);
                }
            }
            RebuildIndex();
        }

        // ---------------------------------------------------------
        // Команды кнопкам (смена сервера/Wi-Fi) — как было
        // ---------------------------------------------------------
        public void SendSetServer(IPAddress ip, IEnumerable<string> macs = null) =>
            _hub.SendSetServer(ip, macs);

        public void SendSetWifi(string ssid, string password, IEnumerable<string> macs = null) =>
            _hub.SendSetWifi(ssid, password, macs);

        public void SendReboot(IEnumerable<string> macs = null) =>
            _hub.SendReboot(macs);

        public void Dispose()
        {
            if (_engine != null)
            {
                // Отписка от TeamsChanged: у нас лямбда, отписаться напрямую не выйдет,
                // но так как ButtonService — синглтон на весь lifecycle, утечки нет.
            }
            _hub?.Dispose();
        }

        // ---------------------------------------------------------
        private void RebuildIndex()
        {
            _macToName.Clear();

            if (_engine?.Teams == null) return;

            foreach (var t in _engine.Teams)
            {
                if (string.IsNullOrWhiteSpace(t.Mac)) continue;
                if (t.Mac.StartsWith("VIRT:", StringComparison.OrdinalIgnoreCase)) continue;
                _macToName[t.Mac] = t.Name;
            }

            _log.Add(LogKind.System, $"Индекс MAC↔имя перестроен ({_macToName.Count})");
        }
    }
}