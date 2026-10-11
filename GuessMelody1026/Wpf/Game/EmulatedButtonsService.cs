using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using GuessMelody.Core.Models;
using GuessMelody.Wpf.Input;
using GuessMelody.Wpf.Logging;

namespace GuessMelody.Wpf.Game
{
    /// <summary>
    /// Эмуляция нажатий 6 кнопок через F1–F6.
    /// Работает в любом окне приложения.
    /// </summary>
    public sealed class EmulatedButtonsService : IDisposable
    {
        private readonly GlobalHotkeyManager _hotkeys;

        public EmulatedButtonsService(Window owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));

            _hotkeys = new GlobalHotkeyManager(owner);

            // F1..F6 = слоты 0..5
            for (int i = 0; i < 6; i++)
            {
                var key = (Key)((int)Key.F1 + i);
                int slot = i;
                var modifier = ModifierKeys.None; // Ctrl+F1..F6
                bool ok = _hotkeys.Register(modifier, key,
                    () => OnHotkey(slot));

                if (ok)
                    AppLogger.Instance.Info($"[Emu] Горячая клавиша {key} модификатор {modifier} → слот {slot + 1}");
                else
                    AppLogger.Instance.Error($"[Emu] Не удалось зарегистрировать {key} " +
                                             $"(занято другим приложением?)");
            }
        }

        private void OnHotkey(int slot0Based)
        {
            var mac = ResolveSlotMac(slot0Based);
            if (string.IsNullOrEmpty(mac)) return;
            AppLogger.Instance.Info(
       $"[Hotkey] Нажата F{slot0Based + 1} → MAC {mac}");
            EmulatePress(mac);
        }

        private string ResolveSlotMac(int slot0Based)
        {
            var svc = AppServices.ButtonService;
            if (svc == null)
                return $"EMU:{slot0Based + 1:D2}";

            var all = svc.GetAll();

            int targetSlot = slot0Based + 1;

            var bySlot = all.FirstOrDefault(b => b.PlayerSlot == targetSlot);
            if (bySlot != null) return bySlot.Mac;

            if (slot0Based < all.Count)
                return all[slot0Based].Mac;

            return $"EMU:{targetSlot:D2}";
        }

        private void EmulatePress(string mac)
        {
            var svc = AppServices.ButtonService;

            if (svc != null)
            {
                // Обычный путь: PressReceived → GameController → RoundEngine
                svc.EmulatePress(mac);
                return;
            }

            // Сервер не запущен — толкаем напрямую в RoundEngine
            var gc = GameController.Instance;
            if (gc.Engine != null)
            {
                gc.Engine.OnPress(new PressEvent
                {
                    Mac = mac,
                    Seq = Guid.NewGuid().ToString("N").Substring(0, 8),
                    ReceivedUtc = DateTime.UtcNow,
                    RemoteIp = System.Net.IPAddress.Parse("127.0.0.1")
                });
            }
            else
            {
                AppLogger.Instance.Press($"[EMU-F] {mac} (сервер кнопок не запущен)");
            }
        }

        public void Dispose() => _hotkeys?.Dispose();
    }
}