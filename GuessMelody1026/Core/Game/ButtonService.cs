using GuessMelody.Core.Models;
using GuessMelody.Wpf.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;


namespace GuessMelody.Core.Game
{
    /// <summary>
    /// Игровая обёртка над EspButtonHub.
    /// - хранит привязки MAC → DisplayName
    /// - конвертирует события Hub в PressEvent (M1)
    /// - поддерживает «эмуляцию» нажатий для отладки
    /// </summary>
    public  class ButtonService : IDisposable
    {
        private readonly  EspButtonHub _hub;
        
        private readonly ConcurrentDictionary<string, ButtonSnapshot> _snapshots
            = new ConcurrentDictionary<string, ButtonSnapshot>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, ButtonBinding> _bindings
            = new ConcurrentDictionary<string, ButtonBinding>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Пришло валидное нажатие от живой кнопки.</summary>
        public event EventHandler<PressEvent> PressReceived;

        /// <summary>Кнопка подключилась/потеряна/вернулась.</summary>
        public event EventHandler<ButtonSnapshot> ButtonConnected;
        public event EventHandler<ButtonSnapshot> ButtonReconnected;
        public event EventHandler<ButtonSnapshot> ButtonDisconnected;
        public event EventHandler<ButtonEventArgs> Heartbeat;

        /// <summary>Любое обновление — удобно для «просто перерисовать список».</summary>
        public event EventHandler<string> RawLog;

        public ButtonService(EspButtonHub hub)
        {
            // Проверка аргумента
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            // Подписка на события Hub
            _hub.ButtonConnected += Hub_ButtonConnected;
            _hub.ButtonReconnected += Hub_ButtonReconnected;
            _hub.ButtonDisconnected += Hub_ButtonDisconnected;
            _hub.PressReceived += Hub_PressReceived;
            _hub.RawLog += (s, m) => RawLog?.Invoke(this, m);

            // Подключаем логирование в AppLogger
            _hub.RawLog += (s, m) => AppLogger.Instance.Raw(m);
            _hub.ButtonConnected += (s, e) => AppLogger.Instance.Info($"🟢 Кнопка подключилась: {e.Button.Mac}");
            _hub.ButtonReconnected += (s, e) => AppLogger.Instance.Info($"🔗 Кнопка вернулась: {e.Button.Mac}");
            _hub.ButtonDisconnected += (s, e) => AppLogger.Instance.Info($"🔌 Кнопка потеряна: {e.Button.Mac}");
            _hub.CommandAck += (s, e) => AppLogger.Instance.Info($"[ACK {e.From}] {e.Payload}");

        }

        // =============================================================
        // Публичное API
        // =============================================================

        /// <summary>Снимок всех известных кнопок (включая не привязанные).</summary>
        public IReadOnlyList<ButtonSnapshot> GetAll()
        {
            // Перед отдачей — обновим время «последний раз видели» из Hub
            SyncFromHub();
            return _snapshots.Values
                .OrderBy(b => b.PlayerSlot ?? int.MaxValue)
                .ThenBy(b => b.Mac, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public ButtonSnapshot GetByMac(string mac) =>
            _snapshots.TryGetValue(mac, out var s) ? s : null;

        /// <summary>Привязать имя к MAC-у.</summary>
        public void Bind(string mac, string displayName, int? playerSlot = null)
        {
            if (string.IsNullOrWhiteSpace(mac)) return;

            var binding = new ButtonBinding
            {
                Mac = mac,
                DisplayName = displayName ?? "",
                PlayerSlot = playerSlot
            };
            _bindings[mac] = binding;

            if (_snapshots.TryGetValue(mac, out var s))
            {
                s.DisplayName = binding.DisplayName;
                s.PlayerSlot = binding.PlayerSlot;
            }
        }

        public void Unbind(string mac)
        {
            if (string.IsNullOrWhiteSpace(mac)) return;
            _bindings.TryRemove(mac, out _);
            if (_snapshots.TryGetValue(mac, out var s))
            {
                s.DisplayName = null;
                s.PlayerSlot = null;
            }
        }

        /// <summary>Применить весь набор привязок из JSON.</summary>
        public void ApplyBindings(IEnumerable<ButtonBinding> bindings)
        {
            if (bindings == null) return;
            foreach (var b in bindings)
                Bind(b.Mac, b.DisplayName, b.PlayerSlot);
        }

        /// <summary>Экспортировать текущие привязки.</summary>
        public ButtonBindings ExportBindings()
        {
            return new ButtonBindings
            {
                SchemaVersion = 1,
                Bindings = _bindings.Values
                    .OrderBy(b => b.PlayerSlot ?? int.MaxValue)
                    .ThenBy(b => b.Mac)
                    .ToList()
            };
        }

        /// <summary>
        /// Эмулировать нажатие от указанного MAC — для тестов без физической кнопки.
        /// Идёт по тому же пути, что и реальное нажатие.
        /// </summary>
        public void EmulatePress(string mac)
        {
            if (string.IsNullOrEmpty(mac)) return;
            var press = new PressEvent
            {
                Mac = mac,
                Seq = Guid.NewGuid().ToString("N").Substring(0, 8),
                UptimeMs = 0,
                RemoteIp = System.Net.IPAddress.Parse("127.0.0.1"),
                ReceivedUtc = DateTime.UtcNow
            };

            // Убедимся, что кнопка вообще известна (для UI).
            EnsureSnapshot(mac, DateTime.UtcNow);

            PressReceived?.Invoke(this, press);
            RawLog?.Invoke(this, $"[EMU] press from {mac}");
        }

        // =============================================================
        // Обработчики Hub
        // =============================================================

        private void Hub_ButtonConnected(object sender, ButtonEventArgs e)
        {
            var snap = UpdateSnapshot(e.Button);
            ButtonConnected?.Invoke(this, snap);
        }

        private void Hub_ButtonReconnected(object sender, ButtonEventArgs e)
        {
            var snap = UpdateSnapshot(e.Button);
            ButtonReconnected?.Invoke(this, snap);
        }

        private void Hub_ButtonDisconnected(object sender, ButtonEventArgs e)
        {
            var snap = UpdateSnapshot(e.Button);
            ButtonDisconnected?.Invoke(this, snap);
        }

        private void Hub_PressReceived(object sender, PressEventArgs e)
        {
            var snap = UpdateSnapshot(e.Button);
            var press = new PressEvent
            {
                Mac = e.Button.Mac,
                Seq = e.Seq,
                UptimeMs = e.UptimeMs,
                RemoteIp = e.RemoteIp,
                ReceivedUtc = e.ReceivedUtc
            };
            PressReceived?.Invoke(this, press);
        }

        // =============================================================
        // Внутреннее
        // =============================================================

        private void SyncFromHub()
        {
            foreach (var info in _hub.GetButtons())
                UpdateSnapshot(info);
        }

        private ButtonSnapshot UpdateSnapshot(ButtonInfo info)
        {
            var snap = _snapshots.GetOrAdd(info.Mac, _ => new ButtonSnapshot
            {
                Mac = info.Mac,
                FirstSeenUtc = info.FirstSeenUtc
            });

            // Применим привязку, если есть
            if (_bindings.TryGetValue(info.Mac, out var b))
            {
                snap.DisplayName = b.DisplayName;
                snap.PlayerSlot = b.PlayerSlot;
            }

            snap.LastIp = info.LastIp?.ToString();
            snap.Alive = info.Alive;
            snap.LastSeenUtc = info.LastSeenUtc;
            snap.PressCount = info.PressCount;
            snap.LastSeq = info.LastSeq;
            snap.LastUptimeMs = info.LastUptimeMs;
            snap.HbReceived = info.ReceivedHbCount;
            snap.HbExpected = info.ExpectedHbCount;
            snap.DeliveryPercent = info.DeliveryPercent;

            return snap;
        }

        private void EnsureSnapshot(string mac, DateTime seen)
        {
            _snapshots.GetOrAdd(mac, _ => new ButtonSnapshot
            {
                Mac = mac,
                FirstSeenUtc = seen,
                LastSeenUtc = seen,
                Alive = true
            });
        }

        public void Dispose()
        {
            _hub.ButtonConnected -= Hub_ButtonConnected;
            _hub.ButtonReconnected -= Hub_ButtonReconnected;
            _hub.ButtonDisconnected -= Hub_ButtonDisconnected;
            _hub.PressReceived -= Hub_PressReceived;
        }
    }
}