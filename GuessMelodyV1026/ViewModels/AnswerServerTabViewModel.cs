using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Windows.Input;
using System.Windows.Threading;
using GuessMelody.AnswerServer;
using GuessMelody.Core;
using GuessMelody.Core.Enums;
using GuessMelody.Services;

namespace GuessMelody.ViewModels
{
    public class AnswerServerTabViewModel : ViewModelBase, IDisposable
    {
        private readonly AnswerHttpServer _server;
        private readonly AnswerStatePublisher _publisher;
        private readonly GameEngine _engine;
        private readonly LogService _log;
        private readonly DialogService _dlg;
        private readonly DispatcherTimer _infoTimer;

        public AnswerServerTabViewModel(
            GameEngine engine,
            LogService log,
            DialogService dlg)
        {
            _engine = engine;
            _log = log;
            _dlg = dlg;

            _publisher = new AnswerStatePublisher(_engine);
            _server = new AnswerHttpServer(_publisher);
            _server.Log += (s, line) => _log.Add(LogKind.Http, line);

            // связываем кнопки веб-страницы с движком
            _server.OnScoreYes = () =>
            {
                _engine.SubmitScore(true);
                _log.Add(LogKind.Http, "→ POST /score/yes");
            };
            _server.OnScoreNo = () =>
            {
                _engine.SubmitScore(false);
                _log.Add(LogKind.Http, "→ POST /score/no");
            };
            _server.OnNext = () =>
            {
                _engine.NextRound();
                _log.Add(LogKind.Http, "→ POST /next");
            };
            _server.OnPanic = () =>
            {
                _engine.Panic();
                _log.Add(LogKind.Http, "→ POST /panic");
            };

            StartCommand = new RelayCommand(_ => Start(), _ => !IsRunning);
            StopCommand = new RelayCommand(_ => Stop(), _ => IsRunning);
            OpenBrowserCommand = new RelayCommand(_ => OpenBrowser());
            CopyUrlCommand = new RelayCommand(_ => CopyUrl());

            // обновлять состояние страницы раз в секунду, если включён
            _infoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _infoTimer.Tick += (_, __) =>
            {
                OnPropertyChanged(nameof(CurrentStatusLine));
                OnPropertyChanged(nameof(CurrentUrl));
            };
            _infoTimer.Start();
        }

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand OpenBrowserCommand { get; }
        public ICommand CopyUrlCommand { get; }

        private int _port = 8080;
        public int Port
        {
            get => _port;
            set { if (Set(ref _port, value)) OnPropertyChanged(nameof(CurrentUrl)); }
        }

        private string _title = "Угадай мелодию";
        public string Title
        {
            get => _title;
            set { if (Set(ref _title, value)) _publisher.Title = value; }
        }

        private string _placeholder = "";
        public string Placeholder
        {
            get => _placeholder;
            set { if (Set(ref _placeholder, value)) _publisher.PlaceholderText = value; }
        }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                Set(ref _isRunning, value);
                OnPropertyChanged(nameof(CurrentUrl));
                OnPropertyChanged(nameof(CurrentStatusLine));
            }
        }

        // для биндинга на текущий URL
        public string CurrentUrl
        {
            get
            {
                if (!IsRunning) return "";
                var ip = GetPrimaryIpv4();
                return $"http://{ip}:{Port}/";
            }
        }

        public string CurrentStatusLine
        {
            get
            {
                if (!IsRunning) return "Сервер остановлен.";
                var ip = GetPrimaryIpv4();
                return $"Сервер работает: http://{ip}:{Port}/";
            }
        }

        // ---------------------------------------------------------
        private void Start()
        {
            _publisher.Title = Title;
            _publisher.PlaceholderText = Placeholder;
            _server.Start(Port);
            IsRunning = _server.IsRunning;

            if (IsRunning)
                _log.Add(LogKind.Http, $"HTTP-сервер запущен на порту {Port}");
            else
                _log.Add(LogKind.Error, "HTTP-сервер не запустился.");
        }

        private void Stop()
        {
            _server.Stop();
            IsRunning = false;
            _log.Add(LogKind.Http, "HTTP-сервер остановлен.");
        }

        private void OpenBrowser()
        {
            var url = IsRunning ? CurrentUrl : $"http://localhost:{Port}/";
            try { Process.Start(url); }
            catch (Exception ex) { _dlg.Error(ex.Message); }
        }

        private void CopyUrl()
        {
            if (!IsRunning) return;
            try
            {
                System.Windows.Clipboard.SetText(CurrentUrl);
                _log.Add(LogKind.Http, "URL скопирован в буфер обмена.");
            }
            catch (Exception ex) { _dlg.Error("Clipboard: " + ex.Message); }
        }

        // ---------------------------------------------------------
        private static string GetPrimaryIpv4()
        {
            try
            {
                // Ищем в порядке приоритета:
                // 1. 192.168.137.x (мобильный хот-спот Windows)
                // 2. первый приватный IPv4
                // 3. любой IPv4
                var addrs = Dns.GetHostAddresses(Dns.GetHostName())
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                    .ToList();

                var hot = addrs.FirstOrDefault(a => a.ToString().StartsWith("192.168.137."));
                if (hot != null) return hot.ToString();

                var privateIp = addrs.FirstOrDefault(a =>
                {
                    var b = a.GetAddressBytes();
                    return b[0] == 10 ||
                          (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
                          (b[0] == 192 && b[1] == 168);
                });
                if (privateIp != null) return privateIp.ToString();

                return addrs.FirstOrDefault()?.ToString() ?? "localhost";
            }
            catch { return "localhost"; }
        }

        public void Dispose()
        {
            _infoTimer?.Stop();
            _server?.Dispose();
            _publisher?.Dispose();
        }
    }
}