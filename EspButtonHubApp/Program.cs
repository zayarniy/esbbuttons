using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace EspButtonDiag
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("=== EspButtonHub диагностика ===");
            Console.WriteLine();

            // Аргументы: список MAC-ов, которых ждём (опционально)
            // Пример: EspButtonDiag.exe AA:BB:CC:DD:EE:01 AA:BB:CC:DD:EE:02
            var expectedMacs = args.Where(a => a.Contains(":")).ToList();

            int listenPort = 41234;
            int ackPort = 41235;

            using (var hub = new EspButtonHub(listenPort, ackPort,
                                              heartbeatTimeout: TimeSpan.FromSeconds(30),
                                              watchdogInterval: TimeSpan.FromSeconds(1)))
            {
                hub.RawLog += (s, line) => Console.WriteLine("  " + line);
                hub.ButtonConnected += OnConnected;
                hub.ButtonReconnected += OnReconnected;
                hub.ButtonDisconnected += OnDisconnected;
                hub.PressReceived += OnPress;

                hub.Start();

                if (expectedMacs.Count > 0)
                {
                    Console.WriteLine($"⏳ Ожидаю кнопки: {string.Join(", ", expectedMacs)} (60 c)");
                    var waiter = new Thread(() =>
                    {
                        bool ok = hub.WaitForButtons(expectedMacs,
                                                     TimeSpan.FromSeconds(60),
                                                     out var missing);
                        if (ok)
                            Console.WriteLine("✅ Все ожидаемые кнопки на связи!");
                        else
                            Console.WriteLine($"⏱ Таймаут. Нет: {string.Join(", ", missing)}");
                    })
                    { IsBackground = true };
                    waiter.Start();
                }

                // Строка статуса
                var statusThread = new Thread(() => StatusLoop(hub))
                {
                    IsBackground = true
                };
                statusThread.Start();

                Console.WriteLine();
                Console.WriteLine("Нажмите Ctrl+C для выхода.");
                Console.WriteLine();

                var exit = new ManualResetEventSlim(false);
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    exit.Set();
                };
                exit.Wait();

                hub.Stop();
            }

            Console.WriteLine("Bye.");
        }

        // ---------------- Обработчики событий ----------------
        private static void OnConnected(object sender, ButtonEventArgs e)
        {
            WriteColored(ConsoleColor.Green,
                $"🟢 [{e.TimestampUtc:HH:mm:ss.fff}] НОВАЯ КНОПКА: {e.Button.Mac}  ip={e.Button.LastIp}");
        }

        private static void OnReconnected(object sender, ButtonEventArgs e)
        {
            WriteColored(ConsoleColor.Cyan,
                $"🔗 [{e.TimestampUtc:HH:mm:ss.fff}] ВЕРНУЛАСЬ: {e.Button.Mac}  ip={e.Button.LastIp}");
        }

        private static void OnDisconnected(object sender, ButtonEventArgs e)
        {
            WriteColored(ConsoleColor.Yellow,
                $"🔌 [{e.TimestampUtc:HH:mm:ss.fff}] ПОТЕРЯНА: {e.Button.Mac}  " +
                $"(тишина {e.Button.TimeSinceLastSeen.TotalSeconds:F1} с)");
        }

        private static void OnPress(object sender, PressEventArgs e)
        {
            WriteColored(ConsoleColor.Red,
                $"🔴 [{e.ReceivedUtc:HH:mm:ss.fff}] НАЖАТИЕ: {e.Button.Mac}  " +
                $"seq={e.Seq}  uptime={e.UptimeMs} ms  from={e.RemoteIp}");
        }

        // ---------------- Статус-строка ----------------
        private static void StatusLoop(EspButtonHub hub)
        {
            while (true)
            {
                Thread.Sleep(2000);
                var all = hub.GetButtons();
                var alive = hub.GetAliveButtons();

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"── [{DateTime.Now:HH:mm:ss}] живых {alive.Count}/{all.Count}: ");
                Console.WriteLine(string.Join(", ",
                    all.Select(b => (b.Alive ? "●" : "○") + b.Mac + $"({b.PressCount})")));
                Console.ResetColor();
            }
        }

        private static void WriteColored(ConsoleColor color, string text)
        {
            var old = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = old;
        }
    }
}