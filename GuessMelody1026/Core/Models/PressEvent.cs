using System;
using System.Net;

namespace GuessMelody.Core.Models
{
    public class PressEvent
    {
        public string Mac { get; set; }
        public string Seq { get; set; }
        public long UptimeMs { get; set; }
        public IPAddress RemoteIp { get; set; }

        // "Когда на самом деле" — серверное время приёма пакета.
        // Именно оно используется как арбитр «кто первый».
        public DateTime ReceivedUtc { get; set; }

        // Задел на синхронизацию: серверное время можно скорректировать
        // через оценку смещения часов клиента.
        public DateTime? ServerSyncedUtc { get; set; }
    }
}