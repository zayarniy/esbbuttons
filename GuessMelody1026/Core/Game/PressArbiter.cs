using System;
using System.Collections.Generic;
using System.Linq;
using GuessMelody.Core.Models;

namespace GuessMelody.Core.Game
{
    /// <summary>
    /// Арбитр нажатий в рамках одного раунда.
    /// Определяет «кто первый», кто уже отвечал и не угадал,
    /// и кого считать победителем.
    /// </summary>
    public sealed class PressArbiter
    {
        // Все нажатия в порядке приёма
        private readonly List<PressEvent> _presses = new List<PressEvent>();

        // MAC-и, которые уже отвечали «Нет» — их игнорируем до конца раунда
        private readonly HashSet<string> _rejectedMacs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<PressEvent> Presses => _presses;

        /// <summary>Сброс в начале нового раунда.</summary>
        public void Reset()
        {
            _presses.Clear();
            _rejectedMacs.Clear();
        }

        /// <summary>
        /// Обработать входящее нажатие.
        /// Возвращает true, если нажатие принято как «кандидат в первые».
        /// </summary>
        public bool OnPress(PressEvent e)
        {
            if (e == null || string.IsNullOrEmpty(e.Mac)) return false;
            if (_rejectedMacs.Contains(e.Mac)) return false;

            // Дубликаты одного и того же пакета (по MAC+Seq) отбрасываем.
            if (_presses.Any(p => SameIdentity(p, e)))
                return false;

            _presses.Add(e);
            SortByEffectiveTime();
            return true;
        }

        /// <summary>Кто первый в текущем раунде (первый не отброшенный).</summary>
        public PressEvent GetFirst()
        {
            return _presses
                .Where(p => !_rejectedMacs.Contains(p.Mac))
                .OrderBy(EffectiveTime)
                .FirstOrDefault();
        }

        /// <summary>Пометить MAC как «ответил неверно» — больше не участвует.</summary>
        public void Reject(string mac)
        {
            if (string.IsNullOrEmpty(mac)) return;
            _rejectedMacs.Add(mac);
        }

        /// <summary>Сколько разных MAC реально участвовало.</summary>
        public int DistinctParticipants =>
            _presses.Select(p => p.Mac).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        /// <summary>Все ли известные MAC уже отброшены (играть больше некому).</summary>
        public bool EveryoneRejected(IEnumerable<string> knownMacs)
        {
            var known = knownMacs.Where(m => !string.IsNullOrEmpty(m))
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .ToList();
            if (known.Count == 0) return false;
            return known.All(m => _rejectedMacs.Contains(m));
        }

        private static bool SameIdentity(PressEvent a, PressEvent b) =>
            string.Equals(a.Mac, b.Mac, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.Seq, b.Seq, StringComparison.Ordinal);

        /// <summary>
        /// Эффективное время для сортировки.
        /// Сейчас — серверное ReceivedUtc. Задел на будущее: если появится
        /// ServerSyncedUtc (уточнённое клиентское время), использовать его.
        /// </summary>
        private static DateTime EffectiveTime(PressEvent p) =>
            p.ServerSyncedUtc ?? p.ReceivedUtc;

        private void SortByEffectiveTime()
        {
            _presses.Sort((a, b) => EffectiveTime(a).CompareTo(EffectiveTime(b)));
        }
    }
}