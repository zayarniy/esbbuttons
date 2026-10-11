using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GuessMelody.Core;

namespace GuessMelody.AnswerServer
{
    public class AnswerHttpServer : IDisposable
    {
        private HttpListener _listener;
        private bool _running;
        private CancellationTokenSource _cts;

        public int Port { get; private set; }
        public bool IsRunning => _running;

        public AnswerStatePublisher Publisher { get; }
        public Action OnScoreYes { get; set; }
        public Action OnScoreNo { get; set; }
        public Action OnNext { get; set; }
        public Action OnPanic { get; set; }

        public event EventHandler<string> Log;

        public AnswerHttpServer(AnswerStatePublisher publisher)
        {
            Publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        }

        public void Start(int port)
        {
            if (_running) return;

            Port = port;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{port}/");
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");

            try
            {
                _listener.Start();
            }
            catch (HttpListenerException ex)
            {
                Log?.Invoke(this,
                    $"Не удалось запустить HTTP на порту {port}: {ex.Message}. " +
                    $"Возможные причины: порт занят, нужны права администратора или urlacl. " +
                    $"Попробуйте: netsh http add urlacl url=http://+:{port}/ user=Everyone");
                _listener = null;
                return;
            }

            _cts = new CancellationTokenSource();
            _running = true;
            Task.Run(() => Loop(_cts.Token));
            Log?.Invoke(this, $"HTTP-сервер запущен на порту {port}");
        }

        public void Stop()
        {
            _running = false;
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
            _listener = null;
            _cts = null;
            Log?.Invoke(this, "HTTP-сервер остановлен.");
        }

        public void Dispose() => Stop();

        // ---------------------------------------------------------
        private async Task Loop(CancellationToken token)
        {
            while (_running && _listener != null && _listener.IsListening && !token.IsCancellationRequested)
            {
                HttpListenerContext ctx = null;
                try
                {
                    ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch
                {
                    if (!_running) return;
                    continue;
                }

                try
                {
                    await HandleRequest(ctx).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log?.Invoke(this, "HTTP error: " + ex.Message);
                    try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
                }
            }
        }

        private async Task HandleRequest(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var resp = ctx.Response;
            var path = req.Url?.AbsolutePath ?? "/";
            var method = req.HttpMethod?.ToUpperInvariant() ?? "GET";

            Log?.Invoke(this, $"{method} {path} ← {req.RemoteEndPoint?.Address}");

            // ---------- GET /state ----------
            if (path.Equals("/state", StringComparison.OrdinalIgnoreCase) && method == "GET")
            {
                var snap = Publisher.Build();
                var json = SnapshotToJson(snap);
                var bytes = Encoding.UTF8.GetBytes(json);
                resp.ContentType = "application/json; charset=utf-8";
                resp.ContentLength64 = bytes.Length;
                resp.Headers["Cache-Control"] = "no-store";
                await resp.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                resp.OutputStream.Close();
                return;
            }

            // ---------- POST /score/yes ----------
            if (path.Equals("/score/yes", StringComparison.OrdinalIgnoreCase) && method == "POST")
            {
                OnScoreYes?.Invoke();
                await WriteJson(resp, "{\"ok\":true}").ConfigureAwait(false);
                return;
            }

            // ---------- POST /score/no ----------
            if (path.Equals("/score/no", StringComparison.OrdinalIgnoreCase) && method == "POST")
            {
                OnScoreNo?.Invoke();
                await WriteJson(resp, "{\"ok\":true}").ConfigureAwait(false);
                return;
            }

            // ---------- POST /next ----------
            if (path.Equals("/next", StringComparison.OrdinalIgnoreCase) && method == "POST")
            {
                OnNext?.Invoke();
                await WriteJson(resp, "{\"ok\":true}").ConfigureAwait(false);
                return;
            }

            // ---------- POST /panic ----------
            if (path.Equals("/panic", StringComparison.OrdinalIgnoreCase) && method == "POST")
            {
                OnPanic?.Invoke();
                await WriteJson(resp, "{\"ok\":true}").ConfigureAwait(false);
                return;
            }

            // ---------- GET / ----------
            if (path.Equals("/", StringComparison.OrdinalIgnoreCase) && method == "GET")
            {
                var html = AnswerPageRenderer.Render(this);
                var bytes = Encoding.UTF8.GetBytes(html);
                resp.ContentType = "text/html; charset=utf-8";
                resp.ContentLength64 = bytes.Length;
                resp.Headers["Cache-Control"] = "no-store";
                await resp.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                resp.OutputStream.Close();
                return;
            }

            // ---------- 404 ----------
            resp.StatusCode = 404;
            resp.ContentType = "text/plain; charset=utf-8";
            var msg = Encoding.UTF8.GetBytes("Not found");
            resp.ContentLength64 = msg.Length;
            await resp.OutputStream.WriteAsync(msg, 0, msg.Length).ConfigureAwait(false);
            resp.OutputStream.Close();
        }

        private static async Task WriteJson(HttpListenerResponse resp, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            resp.ContentType = "application/json; charset=utf-8";
            resp.ContentLength64 = bytes.Length;
            resp.Headers["Cache-Control"] = "no-store";
            await resp.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            resp.OutputStream.Close();
        }

        /// <summary>Простейший ручной JSON — без зависимостей.</summary>
        private static string SnapshotToJson(GameStateSnapshot s)
        {
            var sb = new StringBuilder(512);
            sb.Append('{');
            sb.Append("\"revision\":").Append(s.Revision).Append(',');
            sb.Append("\"title\":").Append(JsonStr(s.Title)).Append(',');
            sb.Append("\"gameName\":").Append(JsonStr(s.GameName)).Append(',');
            sb.Append("\"roundText\":").Append(JsonStr(s.RoundText)).Append(',');
            sb.Append("\"stateText\":").Append(JsonStr(s.StateText)).Append(',');
            sb.Append("\"categoryText\":").Append(JsonStr(s.CategoryText)).Append(',');
            sb.Append("\"trackText\":").Append(JsonStr(s.TrackText)).Append(',');
            sb.Append("\"firstPressedText\":").Append(JsonStr(s.FirstPressedText)).Append(',');
            sb.Append("\"placeholderText\":").Append(JsonStr(s.PlaceholderText)).Append(',');
            sb.Append("\"trackLeftSec\":").Append(s.TrackLeftSec.ToString("F1",
                System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"trackTotalSec\":").Append(s.TrackTotalSec.ToString("F1",
                System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"answerLeftSec\":").Append(s.AnswerLeftSec.ToString("F1",
                System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"serverTime\":").Append(JsonStr(s.ServerTime.ToString("HH:mm:ss"))).Append(',');

            // teams
            sb.Append("\"teams\":[");
            for (int i = 0; i < s.Teams.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var t = s.Teams[i];
                sb.Append('{');
                sb.Append("\"name\":").Append(JsonStr(t.Name)).Append(',');
                sb.Append("\"score\":").Append(t.Score).Append(',');
                sb.Append("\"firstPressed\":").Append(t.FirstPressed ? "true" : "false");
                sb.Append('}');
            }
            sb.Append("],");

            // categories
            sb.Append("\"categories\":[");
            for (int i = 0; i < s.Categories.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var c = s.Categories[i];
                sb.Append('{');
                sb.Append("\"name\":").Append(JsonStr(c.Name)).Append(',');
                sb.Append("\"total\":").Append(c.Total).Append(',');
                sb.Append("\"remaining\":").Append(c.Remaining).Append(',');
                sb.Append("\"isActive\":").Append(c.IsActive ? "true" : "false");
                sb.Append('}');
            }
            sb.Append(']');

            sb.Append('}');
            return sb.ToString();
        }

        private static string JsonStr(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}