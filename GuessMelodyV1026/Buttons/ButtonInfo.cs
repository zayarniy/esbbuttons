using GuessMelody.Core;
using GuessMelody.Core.Enums;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;




public class ButtonsConfig
{
    public List<ButtonBinding> Bindings { get; set; } = new List<ButtonBinding>();
}
public class ButtonBinding
{
    public int TeamId { get; set; }
    public string TeamName { get; set; } = "";
    public string Mac { get; set; } = "";
}
public class EspButtonHub : IDisposable
{
    // -------- Настройки --------
    public int ListenPort { get; }
    public int AckPort { get; }
    public int CmdPort { get; }
    public TimeSpan HeartbeatTimeout { get; }
    public TimeSpan WatchdogInterval { get; }

    // -------- События --------
    public event EventHandler<ButtonEventArgs> ButtonConnected;
    public event EventHandler<ButtonEventArgs> ButtonReconnected;
    public event EventHandler<ButtonEventArgs> ButtonDisconnected;
    public event EventHandler<PressEventArgs> PressReceived;
    public event EventHandler<AckEventArgs> CommandAck;
    public event EventHandler<string> RawLog;
    public event EventHandler<ButtonEventArgs> HeartbeatReceived;

    // -------- Состояние --------
    private readonly ConcurrentDictionary<string, ButtonInfo> _buttons =
        new ConcurrentDictionary<string, ButtonInfo>(StringComparer.OrdinalIgnoreCase);

    private UdpClient _udp;
    private CancellationTokenSource _cts;
    private Task _receiveTask;
    private Task _watchdogTask;

    public EspButtonHub(int listenPort = 41234,
                        int ackPort = 41235,
                        int cmdPort = 41234,
                        TimeSpan? heartbeatTimeout = null,
                        TimeSpan? watchdogInterval = null)
    {
        ListenPort = listenPort;
        AckPort = ackPort;
        CmdPort = cmdPort;
        HeartbeatTimeout = heartbeatTimeout ?? TimeSpan.FromSeconds(30);
        WatchdogInterval = watchdogInterval ?? TimeSpan.FromSeconds(1);
    }

    // ------------------------------------------------------------
    // Жизненный цикл
    // ------------------------------------------------------------
    public void Start()
    {
        if (_udp != null) throw new InvalidOperationException("Уже запущен.");

        _udp = new UdpClient();
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket,
                                    SocketOptionName.ReuseAddress, true);
        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, ListenPort));
        _udp.EnableBroadcast = true;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _receiveTask = Task.Factory.StartNew(
            () => ReceiveLoop(token),
            token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        _watchdogTask = Task.Factory.StartNew(
            () => WatchdogLoop(token),
            token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        Log($"Сервер запущен. UDP:{ListenPort}, CMD:{CmdPort}, ACK:{AckPort}");
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
    // Команды кнопкам
    // ------------------------------------------------------------
    public void SendSetServer(IPAddress newServer, IEnumerable<string> macs = null)
    {
        if (newServer == null) return;
        SendCommand($"SETSRV|{newServer}", macs);
    }

    public void SendSetWifi(string ssid, string password, IEnumerable<string> macs = null)
    {
        if (string.IsNullOrWhiteSpace(ssid)) return;
        SendCommand($"SETWIFI|{ssid}|{password ?? ""}", macs);
    }

    public void SendReboot(IEnumerable<string> macs = null) =>
        SendCommand("REBOOT", macs);

    public void SendGetCfg(IEnumerable<string> macs = null) =>
        SendCommand("GETCFG", macs);

    private void SendCommand(string cmd, IEnumerable<string> macs)
    {
        if (_udp == null)
        {
            Log($"⚠ CMD '{cmd}' пропущена: сервер не запущен.");
            return;
        }

        var bytes = Encoding.ASCII.GetBytes(cmd);

        // null => broadcast
        if (macs == null)
        {
            try
            {
                var ep = new IPEndPoint(IPAddress.Broadcast, CmdPort);
                _udp.Send(bytes, bytes.Length, ep);
                Log($"→ BROADCAST {ep}: {cmd}");
            }
            catch (Exception ex)
            {
                Log($"Broadcast error: {ex.Message}");
            }
            return;
        }

        foreach (var mac in macs)
        {
            if (_buttons.TryGetValue(mac, out var info) && info.LastIp != null)
            {
                try
                {
                    var ep = new IPEndPoint(info.LastIp, CmdPort);
                    _udp.Send(bytes, bytes.Length, ep);
                    Log($"→ CMD {mac} {ep}: {cmd}");
                }
                catch (Exception ex)
                {
                    Log($"CMD error to {mac}: {ex.Message}");
                }
            }
            else
            {
                Log($"⚠ CMD skip {mac}: не знаем IP");
            }
        }
    }

    // ------------------------------------------------------------
    // ReceiveLoop
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
            catch (SocketException ex) { Log($"SocketException: {ex.Message}"); continue; }
            catch (Exception ex) { Log($"Receive error: {ex.Message}"); continue; }

            var receivedUtc = DateTime.UtcNow;
            string text = Encoding.ASCII.GetString(data).TrimEnd('\r', '\n', '\0');

            try { HandlePacket(text, remote, receivedUtc); }
            catch (Exception ex) { Log($"Handle error от {remote}: {ex.Message}"); }
        }
    }

    private void HandlePacket(string text, IPEndPoint remote, DateTime receivedUtc)
    {
        Log($"← [{receivedUtc:HH:mm:ss.fff}] {remote}: {text}");

        // ---- Ответы на команды: OK:, ERR:, CFG| ----
        if (text.StartsWith("OK:", StringComparison.Ordinal) ||
            text.StartsWith("ERR:", StringComparison.Ordinal) ||
            text.StartsWith("CFG|", StringComparison.Ordinal))
        {
            SafeRaise(CommandAck, new AckEventArgs(remote.Address, text, receivedUtc));
            return;
        }

        // ---- Heartbeat: HB|<MAC>|<counter> ----
        if (text.StartsWith("HB|", StringComparison.Ordinal))
        {
            string body = text.Substring(3).Trim();
            int sep = body.IndexOf('|');
            string mac = (sep >= 0 ? body.Substring(0, sep) : body).Trim();
            if (mac.Length == 0) return;

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
                            if (existing.LastHbCounter > 0 && counter < existing.LastHbCounter)
                                rebooted = true;

                            if (rebooted)
                            {
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
            //HeartbeatReceived?.Invoke(this, new ButtonEventArgs(info, receivedUtc));
            SafeRaise(HeartbeatReceived, new ButtonEventArgs(info, receivedUtc));
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
    // Watchdog
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
                    shouldBeDead = b.Alive && (now - b.LastSeenUtc) > HeartbeatTimeout;
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
            var ackEp = new IPEndPoint(client.Address, CmdPort);
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
    private void Log(string s) => SafeRaise(RawLog, s);

    private void SafeRaise<T>(EventHandler<T> handler, T args)
    {

        if (handler == null)
        {
            System.Diagnostics.Debug.WriteLine($"null handeler");
            return;
        }
        try { handler(this, args); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EspButtonHub] handler error: {ex}");
        }
    }
}

public class AckEventArgs : EventArgs
{
    public IPAddress From { get; }
    public string Payload { get; }
    public DateTime ReceivedUtc { get; }

    public AckEventArgs(IPAddress from, string payload, DateTime t)
    {
        From = from;
        Payload = payload;
        ReceivedUtc = t;
    }
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
        Button = b;
        Seq = seq;
        UptimeMs = uptimeMs;
        ReceivedUtc = receivedUtc;
        RemoteIp = remoteIp;
    }
}

public class ButtonEventArgs : EventArgs
{
    public ButtonInfo Button { get; }
    public DateTime TimestampUtc { get; }

    public ButtonEventArgs(ButtonInfo b, DateTime t)
    {
        Button = b;
        TimestampUtc = t;
    }
}

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

    // Heartbeat-статистика
    public uint LastHbCounter { get; internal set; }
    public uint FirstHbCounter { get; internal set; }
    public uint ExpectedHbCount { get; internal set; }
    public uint ReceivedHbCount { get; internal set; }
    public bool Rebooting { get; internal set; }

    public double DeliveryPercent =>
        ExpectedHbCount == 0
            ? 100.0
            : Math.Min(100.0, 100.0 * ReceivedHbCount / ExpectedHbCount);

    public TimeSpan TimeSinceLastSeen => DateTime.UtcNow - LastSeenUtc;

    public override string ToString() =>
        $"{Mac} alive={Alive} ip={LastIp} presses={PressCount} hb={ReceivedHbCount}/{ExpectedHbCount}";
}