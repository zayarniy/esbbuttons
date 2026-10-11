using System;
using GuessMelody.Buttons;
using GuessMelody.Core;
using GuessMelody.Core.Enums;

namespace GuessMelody.Services
{
    /// <summary>
    /// Связывает UDP-кнопки (ButtonService) с игровой логикой (GameEngine).
    /// Каждое реальное нажатие ESP-кнопки превращается в GameEngine.OnPlayerPress.
    /// </summary>
    public class PressBridge : IDisposable
    {
        private readonly ButtonService _buttons;
        private readonly GameEngine _engine;
        private readonly LogService _log;

        public PressBridge(ButtonService buttons, GameEngine engine, LogService log)
        {
            _buttons = buttons ?? throw new ArgumentNullException(nameof(buttons));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _log = log;

            _buttons.Press += OnButtonPress;
        }

        private void OnButtonPress(object sender, PressEventArgs e)
        {
            var mac = e?.Button?.Mac;
            if (string.IsNullOrEmpty(mac)) return;

            // Игнорируем нажатия от кнопок, не привязанных ни к одной команде.
            // Иначе движок залогирует «Игнор нажатия <MAC> — состояние ...», а в UI
            // будет путаница. Плюс — это защита от «случайных» ESP в сети.
            var team = FindTeamByMac(mac);
            if (team == null)
            {
                _log.Add(LogKind.Press,
                    $"⚠ Нажатие от неизвестного MAC {mac} проигнорировано.");
                return;
            }

            _log.Add(LogKind.Press,
                $"🎮 Реальное нажатие: {team.Name} ({mac})");

            // Передаём в движок. Дальше — вся логика раунда, как для эмуляции.
            _engine.OnPlayerPress(mac, e.ReceivedUtc, e.Seq, e.UptimeMs);
        }

        private Core.Models.Team FindTeamByMac(string mac)
        {
            foreach (var t in _engine.Teams)
            {
                if (string.Equals(t.Mac, mac, StringComparison.OrdinalIgnoreCase))
                    return t;
            }
            return null;
        }

        public void Dispose()
        {
            if (_buttons != null) _buttons.Press -= OnButtonPress;
        }
    }
}