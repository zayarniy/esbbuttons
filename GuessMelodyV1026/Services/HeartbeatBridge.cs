using System;
using System.Windows.Threading;
using GuessMelody.Buttons;
using GuessMelody.Core;

namespace GuessMelody.Services
{
    /// <summary>
    /// Связывает ButtonService (UDP кнопок) и GameEngine (логика игры).
    /// - каждый HB → engine.OnHeartbeat(mac)
    /// - тик раз в 1 секунду → engine.TickHeartbeats(11)
    /// </summary>
    public class HeartbeatBridge : IDisposable
    {
        //public const double TimeoutSec = 12.0;
        public double TimeoutSec { get; set; } = 12.0;

        private readonly ButtonService _buttons;
        private readonly GameEngine _engine;
        private readonly LogService _log;
        private readonly DispatcherTimer _tickTimer;

        public HeartbeatBridge(ButtonService buttons, GameEngine engine, LogService log)
        {
            _buttons = buttons ?? throw new ArgumentNullException(nameof(buttons));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _log = log;

            _buttons.Heartbeat += OnHeartbeat;
            _buttons.Press += OnPress;

            _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _tickTimer.Tick += (s, e) =>
            {
                var t = _engine?.Settings?.Timings?.HeartbeatTimeoutSec ?? 12;
                if (t <= 0) t = 12;
                TimeoutSec = t;
                _engine.TickHeartbeats(TimeoutSec);
            };
            _tickTimer.Start();
        }

        private void OnPress(object sender, PressEventArgs e)
        {
            var mac = e?.Button?.Mac;
            if (string.IsNullOrEmpty(mac)) return;
            // Нажатие тоже продлевает «живость»
            _engine.OnHeartbeat(mac);
        }
        private void OnHeartbeat(object sender, ButtonEventArgs e)
        {
            var mac = e?.Button?.Mac;
            if (string.IsNullOrEmpty(mac)) return;

            // Проверяем, что этот MAC вообще привязан к какой-то команде
            // (иначе движок его проигнорирует, но лишний парсинг не хочется делать)
            _engine.OnHeartbeat(mac);
        }

        public void Dispose()
        {
            _tickTimer?.Stop();
            if (_buttons != null)
            {
                _buttons.Heartbeat -= OnHeartbeat;
                _buttons.Press -= OnPress;      // ← НОВОЕ
            }
        }
    }
}