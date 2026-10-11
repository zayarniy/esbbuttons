using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EspButtonDiag
{
    /// <summary>
    /// Состояние одной ESP8266-кнопки.
    /// </summary>
    public class ButtonInfo
    {
        public string Mac { get; internal set; }
        public IPAddress LastIp { get; internal set; }
        public DateTime FirstSeenUtc { get; internal set; }
        public DateTime LastSeenUtc { get; internal set; }
        public bool Alive { get; internal set; }
        public string LastSeq { get; internal set; }
        public long LastUptimeMs { get; internal set; }
        public int PressCount { get; internal set; }

        // ==== НОВОЕ: heartbeat-статистика ====
        public uint LastHbCounter { get; internal set; }   // последний полученный counter
        public uint FirstHbCounter { get; internal set; }  // counter в первом пакете (для расчёта)
        public uint ExpectedHbCount { get; internal set; } // сколько HB должно было прийти
        public uint ReceivedHbCount { get; internal set; } // сколько реально пришло
        public bool Rebooting { get; internal set; }       // counter сбросился

        public double DeliveryPercent =>
            ExpectedHbCount == 0 ? 100.0 :
            Math.Min(100.0, 100.0 * ReceivedHbCount / ExpectedHbCount);

        public TimeSpan TimeSinceLastSeen => DateTime.UtcNow - LastSeenUtc;

        public override string ToString() =>
            $"{Mac} alive={Alive} ip={LastIp} presses={PressCount} hb={ReceivedHbCount}/{ExpectedHbCount}";



        public class ButtonEventArgs : EventArgs
        {
            public ButtonInfo Button { get; }
            public DateTime TimestampUtc { get; }
            public ButtonEventArgs(ButtonInfo b, DateTime t) { Button = b; TimestampUtc = t; }
        }

        public class PressEventArgs : EventArgs
        {
            public ButtonInfo Button { get; }
            public string Seq { get; }
            public long UptimeMs { get; }
            public DateTime ReceivedUtc { get; }
            public IPAddress RemoteIp { get; }

            public PressEventArgs(ButtonInfo b, string seq, long uptimeMs,
                                  DateTime receivedUtc, IPAddress remoteIp)
            {
                Button = b; Seq = seq; UptimeMs = uptimeMs;
                ReceivedUtc = receivedUtc; RemoteIp = remoteIp;
            }
        }

        /// <summary>
        /// UDP-сервер для ESP8266-кнопок.
        /// Принимает heartbeat "HB|MAC", нажатия "MAC|seq|uptime",
        /// отвечает ACK:HB / ACK:&lt;seq&gt;, следит за живостью кнопок.
        /// </summary>
        public class EspButtonHub : IDisposable
        {
            // -------- Настройки --------
            public int ListenPort { get; }
            public int AckPort { get; }
            public TimeSpan HeartbeatTimeout { get; }
            public TimeSpan WatchdogInterval { get; }

            // -------- События --------
            public event EventHandler<ButtonEventArgs> ButtonConnected;
            public event EventHandler<ButtonEventArgs> ButtonReconnected;
            public event EventHandler<ButtonEventArgs> ButtonDisconnected;
            public event EventHandler<PressEventArgs> PressReceived;
            public event EventHandler<string> RawLog;   // для диагностики

            // -------- Состояние --------
            private readonly ConcurrentDictionary<string, ButtonInfo> _buttons =
                new ConcurrentDictionary<string, ButtonInfo>(StringComparer.OrdinalIgnoreCase);

            private UdpClient _udp;
            private CancellationTokenSource _cts;
            private Task _receiveTask;
            private Task _watchdogTask;

            public EspButtonHub(int listenPort = 41234, int ackPort = 41235,
                                TimeSpan? heartbeatTimeout = null,
                                TimeSpan? watchdogInterval = null)
            {
                ListenPort = listenPort;
                AckPort = ackPort;
                HeartbeatTimeout = heartbeatTimeout ?? TimeSpan.FromSeconds(30);
                WatchdogInterval = watchdogInterval ?? TimeSpan.FromSeconds(1);
            }

            // ------------------------------------------------------------
            // Управление жизненным циклом
            // ------------------------------------------------------------
            public void Start()
            {
                if (_udp != null) throw new InvalidOperationException("Уже запущен.");

                _udp = new UdpClient();
                _udp.Client.SetSocketOption(SocketOptionLevel.Socket,
                                            SocketOptionName.ReuseAddress, true);
                _udp.Client.Bind(new IPEndPoint(IPAddress.Any, ListenPort));

                _cts = new CancellationTokenSource();
                var token = _cts.Token;

                _receiveTask = Task.Factory.StartNew(
                    () => ReceiveLoop(token),
                    token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);

                _watchdogTask = Task.Factory.StartNew(
                    () => WatchdogLoop(token),
                    token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);

                Log($"Сервер запущен. Слушаю UDP:{ListenPort}, ACK шлю на порт клиента {AckPort}.");
            }

            public void Stop()
            {
                if (_udp == null) return;

                try { _cts.Cancel(); } catch { }

                try { _udp.Close(); } catch { }
                _udp = null;

                try { _receiveTask?.Wait(1000); } catch { }
                try { _watchdogTask?.Wait(1000); } catch { }

                _cts?.Dispose();
                _cts = null;
                Log("Сервер остановлен.");
            }

            public void Dispose() => Stop();

            // ------------------------------------------------------------
            // Публичный API
            // ------------------------------------------------------------
            public IReadOnlyList<ButtonInfo> GetButtons() =>
                _buttons.Values.OrderBy(b => b.Mac).ToList();

            public IReadOnlyList<ButtonInfo> GetAliveButtons() =>
                _buttons.Values.Where(b => b.Alive).OrderBy(b => b.Mac).ToList();

            public ButtonInfo GetButton(string mac) =>
                _buttons.TryGetValue(mac, out var b) ? b : null;

            /// <summary>
            /// Ожидание, пока все переданные MAC не выйдут на связь.
            /// </summary>
            public bool WaitForButtons(IEnumerable<string> macs, TimeSpan timeout, out List<string> missing)
            {
                var expected = new HashSet<string>(macs, StringComparer.OrdinalIgnoreCase);
                var deadline = DateTime.UtcNow + timeout;

                while (DateTime.UtcNow < deadline)
                {
                    var alive = new HashSet<string>(
                        _buttons.Values.Where(b => b.Alive).Select(b => b.Mac),
                        StringComparer.OrdinalIgnoreCase);

                    missing = expected.Where(m => !alive.Contains(m)).ToList();
                    if (missing.Count == 0) return true;
                    Thread.Sleep(250);
                }

                var aliveEnd = new HashSet<string>(
                    _buttons.Values.Where(b => b.Alive).Select(b => b.Mac),
                    StringComparer.OrdinalIgnoreCase);
                missing = expected.Where(m => !aliveEnd.Contains(m)).ToList();
                return missing.Count == 0;
            }

            // ------------------------------------------------------------
            // Основной цикл приёма
            // ------------------------------------------------------------
            private void ReceiveLoop(CancellationToken token)
            {
                var remote = new IPEndPoint(IPAddress.Any, 0);

                while (!token.IsCancellationRequested)
                {
                    byte[] data;
                    try
                    {
                        data = _udp.Receive(ref remote);
                    }
                    catch (ObjectDisposedException) { return; }
                    catch (SocketException ex)
                    {
                        Log($"SocketException: {ex.Message}");
                        continue;
                    }
                    catch (Exception ex)
                    {
                        Log($"Receive error: {ex.Message}");
                        continue;
                    }

                    var receivedUtc = DateTime.UtcNow;
                    string text = Encoding.ASCII.GetString(data).TrimEnd('\r', '\n', '\0');

                    try
                    {
                        HandlePacket(text, remote, receivedUtc);
                    }
                    catch (Exception ex)
                    {
                        Log($"Handle error для {remote}: {ex.Message}");
                    }
                }
            }

            private void HandlePacket(string text, IPEndPoint remote, DateTime receivedUtc)
            {
                Log($"← [{receivedUtc:HH:mm:ss.fff}] {remote}: {text}");

                // ---- Heartbeat: HB|<MAC>|<counter>  или HB|<MAC> ----
                if (text.StartsWith("HB|", StringComparison.Ordinal))
                {
                    string body = text.Substring(3).Trim();

                    // MAC — до первого '|'
                    int sep = body.IndexOf('|');
                    string mac = (sep >= 0 ? body.Substring(0, sep) : body).Trim();
                    if (mac.Length == 0) return;

                    // counter — опционально
                    uint counter = 0;
                    bool hasCounter = false;
                    if (sep >= 0 && uint.TryParse(body.Substring(sep + 1).Trim(), out var n))
                    {
                        counter = n;
                        hasCounter = true;
                    }

                    bool isNew = false, wasDead = false, rebooted = false;

                    _buttons.AddOrUpdate(mac,
                        _ =>
                        {
                            isNew = true;
                            return new ButtonInfo
                            {
                                Mac = mac,
                                LastIp = remote.Address,
                                FirstSeenUtc = receivedUtc,
                                LastSeenUtc = receivedUtc,
                                Alive = true,
                                FirstHbCounter = counter,
                                LastHbCounter = counter,
                                ReceivedHbCount = 1,
                                ExpectedHbCount = 1
                            };
                        },
                        (_, existing) =>
                        {
                            lock (existing)
                            {
                                wasDead = !existing.Alive;
                                existing.LastIp = remote.Address;
                                existing.LastSeenUtc = receivedUtc;
                                existing.Alive = true;

                                if (hasCounter)
                                {
                                    // Детект перезагрузки: counter уменьшился
                                    if (existing.LastHbCounter > 0 && counter < existing.LastHbCounter)
                                        rebooted = true;

                                    if (rebooted)
                                    {
                                        // Сброс статистики после ребута
                                        existing.FirstHbCounter = counter;
                                        existing.ExpectedHbCount = 1;
                                        existing.ReceivedHbCount = 1;
                                    }
                                    else
                                    {
                                        existing.ExpectedHbCount = counter - existing.FirstHbCounter + 1;
                                        existing.ReceivedHbCount++;
                                    }
                                    existing.LastHbCounter = counter;
                                }
                                else
                                {
                                    existing.ReceivedHbCount++;
                                    existing.ExpectedHbCount++;
                                }
                                existing.Rebooting = rebooted;
                            }
                            return existing;
                        });

                    var info = _buttons[mac];
                    SendAck(remote, "ACK:HB");

                    if (isNew) SafeRaise(ButtonConnected, new ButtonEventArgs(info, receivedUtc));
                    else if (wasDead) SafeRaise(ButtonReconnected, new ButtonEventArgs(info, receivedUtc));
                    else if (rebooted) Log($"♻ {mac} перезагрузилась (HB counter сброшен)");

                    return;
                }



                // ---- Нажатие: MAC|seq|uptime_ms ----
                var parts = text.Split('|');
                if (parts.Length == 3)
                {
                    string mac = parts[0].Trim();
                    string seq = parts[1].Trim();
                    long uptime = 0;
                    long.TryParse(parts[2], out uptime);

                    if (mac.Length == 0) return;

                    _buttons.AddOrUpdate(mac,
                        _ => new ButtonInfo
                        {
                            Mac = mac,
                            LastIp = remote.Address,
                            FirstSeenUtc = receivedUtc,
                            LastSeenUtc = receivedUtc,
                            Alive = true,
                            LastSeq = seq,
                            LastUptimeMs = uptime,
                            PressCount = 1
                        },
                        (_, existing) =>
                        {
                            lock (existing)
                            {
                                existing.LastIp = remote.Address;
                                existing.LastSeenUtc = receivedUtc;
                                existing.Alive = true;
                                existing.LastSeq = seq;
                                existing.LastUptimeMs = uptime;
                                existing.PressCount++;
                            }
                            return existing;
                        });

                    var info = _buttons[mac];
                    SendAck(remote, $"ACK:{seq}");
                    SafeRaise(PressReceived, new PressEventArgs(info, seq, uptime, receivedUtc, remote.Address));
                    return;
                }

                Log($"⚠ Неизвестный пакет от {remote}: {text}");
            }

            // ------------------------------------------------------------
            // Watchdog — детектор «потери» кнопок
            // ------------------------------------------------------------
            private void WatchdogLoop(CancellationToken token)
            {
                while (!token.IsCancellationRequested)
                {
                    try { Task.Delay(WatchdogInterval, token).Wait(token); }
                    catch (OperationCanceledException) { return; }
                    catch (AggregateException) { return; }

                    var now = DateTime.UtcNow;
                    foreach (var kv in _buttons)
                    {
                        var b = kv.Value;
                        bool shouldBeDead;
                        lock (b)
                        {
                            shouldBeDead = b.Alive &&
                                (now - b.LastSeenUtc) > HeartbeatTimeout;
                            if (shouldBeDead) b.Alive = false;
                        }
                        if (shouldBeDead)
                            SafeRaise(ButtonDisconnected, new ButtonEventArgs(b, now));
                    }
                }
            }

            // ------------------------------------------------------------
            // ACK
            // ------------------------------------------------------------
            private void SendAck(IPEndPoint client, string payload)
            {
                try
                {
                    var ackEp = new IPEndPoint(client.Address, AckPort);
                    var bytes = Encoding.ASCII.GetBytes(payload);
                    _udp.Send(bytes, bytes.Length, ackEp);
                    Log($"→ ACK к {ackEp}: {payload}");
                }
                catch (Exception ex)
                {
                    Log($"ACK error: {ex.Message}");
                }
            }

            // ------------------------------------------------------------
            // Вспомогательное
            // ------------------------------------------------------------
            private void Log(string s) => SafeRaise(RawLog, s);

            private void SafeRaise<T>(EventHandler<T> handler, T args)
            {
                if (handler == null) return;
                try { handler(this, args); }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[{nameof(EspButtonHub)}] handler error: {ex}");
                }
            }
        }
    }
}