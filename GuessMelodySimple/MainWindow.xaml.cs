using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.IO;


namespace GuessMelodySimple
{
    public partial class MainWindow : Window
    {
        // ================== Настройки ==================
        private const int UDP_LISTEN_PORT = 41234;
        private const int UDP_CMD_PORT = 41235;
        private const string CONFIG_FILE = "players.txt";  // MAC=Имя

        // ================== Состояние ==================
        private readonly Player[] _players = new Player[3];
        private readonly Random _rnd = new Random();

        // Треки: пул + очередь текущего круга (без повторов)
        private readonly List<string> _allTracks = new List<string>();
        private readonly Queue<string> _remainingTracks = new Queue<string>();

        private AudioFileReader _reader;
        private WaveOutEvent _output;
        private DispatcherTimer _posTimer;
        private bool _sliderDragging = false;
        private bool _sliderWasPlaying = false;

        private string _currentTrackPath = null;

        // UDP
        private UdpClient _udp;
        private Thread _udpThread;
        private volatile bool _udpRunning = false;

        // Карта MAC → индекс игрока
        private readonly Dictionary<string, int> _macToPlayer =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Кто первый нажал (индекс), -1 если никто
        private int _firstPressed = -1;

        // Счётчики треков
        private int TracksLeft => _remainingTracks.Count;
        private int TracksTotal => _allTracks.Count;

        // Кто уже нажимал в текущем раунде (индексы 0..2)
        private readonly HashSet<int> _alreadyPressedThisRound = new HashSet<int>();

        // Текущий нажавший (первый, кто ещё не отпал)
        private int _currentPressed = -1;

        // Последняя позиция музыки перед паузой (для возобновления)
        private TimeSpan _pausedPosition = TimeSpan.Zero;

        // Флаг: музыка была на паузе из-за нажатия, нужно возобновить с той же точки
        private bool _resumeAfterContinue = false;
        public MainWindow()
        {
            InitializeComponent();

            _players[0] = new Player { Name = "Игрок 1", ScoreBox = PlayerScore0, NameBox = PlayerName0, Card = PlayerCard0 };
            _players[1] = new Player { Name = "Игрок 2", ScoreBox = PlayerScore1, NameBox = PlayerName1, Card = PlayerCard1 };
            _players[2] = new Player { Name = "Игрок 3", ScoreBox = PlayerScore2, NameBox = PlayerName2, Card = PlayerCard2 };

            // ЛКМ/ПКМ по карточкам
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                _players[i].Card.MouseLeftButtonDown += (_, __) => ChangeScore(idx, +1);
                _players[i].Card.MouseRightButtonDown += (_, __) => ChangeScore(idx, -1);
            }

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        // ============================================================
        // Жизненный цикл
        // ============================================================
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadConfig();
            StartUdpServer();
            UpdateTracksCounter();

            _posTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _posTimer.Tick += (_, __) => UpdatePosition();
            _posTimer.Start();

            SetStatus("Готово. Откройте папку с музыкой.");
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopUdpServer();
            SaveConfig();
            StopMusic();
        }

        // ============================================================
        // Игроки
        // ============================================================
        private void ChangeScore(int idx, int delta)
        {
            _players[idx].Score += delta;
            _players[idx].ScoreBox.Text = _players[idx].Score.ToString();
        }

        private void PlusOne_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string s && int.TryParse(s, out var i))
                ChangeScore(i, +1);
        }

        private void MinusOne_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string s && int.TryParse(s, out var i))
                ChangeScore(i, -1);
        }

        private void PlayerName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!(sender is TextBox tb)) return;
            if (!int.TryParse(tb.Tag?.ToString(), out var idx)) return;
            _players[idx].Name = tb.Text;
        }

        // ============================================================
        // Конфиг (players.txt: MAC=Имя)
        // ============================================================
        private void LoadConfig()
        {
            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CONFIG_FILE);

            if (!File.Exists(path))
            {
                File.WriteAllText(path,
                    "# Соответствие MAC-адресов ESP8266 и имён игроков\r\n" +
                    "# Формат: MAC=Имя\r\n" +
                    "AA:BB:CC:DD:EE:01=Игрок 1\r\n" +
                    "AA:BB:CC:DD:EE:02=Игрок 2\r\n" +
                    "AA:BB:CC:DD:EE:03=Игрок 3\r\n",
                    Encoding.UTF8);
                SetStatus("Создан " + CONFIG_FILE + ". Заполните MAC-адреса и перезапустите.");
                return;
            }

            var lines = File.ReadAllLines(path, Encoding.UTF8);
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string mac = line.Substring(0, eq).Trim();
                string name = line.Substring(eq + 1).Trim();
                if (mac.Length == 0 || name.Length == 0) continue;

                if (_macToPlayer.Count < 3)
                {
                    int idx = _macToPlayer.Count;
                    _macToPlayer[mac] = idx;
                    _players[idx].Name = name;
                    _players[idx].NameBox.Text = name;
                    _players[idx].Mac = mac;
                }
            }

            SetStatus($"Загружено {_macToPlayer.Count} MAC-адресов из {CONFIG_FILE}");
        }

        private void SaveConfig()
        {
            try
            {
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CONFIG_FILE);
                var sb = new StringBuilder();
                sb.AppendLine("# Соответствие MAC-адресов ESP8266 и имён игроков");
                sb.AppendLine("# Формат: MAC=Имя");
                for (int i = 0; i < 3; i++)
                {
                    string mac = _players[i].Mac ?? $"__PLAYER{i + 1}_MAC__";
                    sb.AppendLine($"{mac}={_players[i].NameBox.Text}");
                }
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                SetStatus("Ошибка сохранения: " + ex.Message);
            }
        }

        // ============================================================
        // UDP-сервер
        // ============================================================
        private void StartUdpServer()
        {
            try
            {
                _udp = new UdpClient();
                _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udp.Client.Bind(new IPEndPoint(IPAddress.Any, UDP_LISTEN_PORT));

                _udpRunning = true;
                _udpThread = new Thread(UdpLoop) { IsBackground = true };
                _udpThread.Start();

                SetStatus($"UDP-сервер запущен на порту {UDP_LISTEN_PORT}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Не удалось запустить UDP-сервер на порту {UDP_LISTEN_PORT}.\n\n{ex.Message}\n\n" +
                    "Возможные причины:\n" +
                    "• порт занят другим приложением;\n" +
                    "• файрвол блокирует приложение;\n" +
                    "• приложение уже запущено в другой копии.",
                    "Ошибка UDP", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopUdpServer()
        {
            _udpRunning = false;
            try { _udp?.Close(); } catch { }
            _udp = null;
        }

        private void UdpLoop()
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (_udpRunning)
            {
                try
                {
                    byte[] data = _udp.Receive(ref remote);
                    string text = Encoding.ASCII.GetString(data).TrimEnd('\r', '\n', '\0');

                    TrySendAck(remote, text);

                    if (text.StartsWith("HB|", StringComparison.Ordinal))
                        continue;

                    var parts = text.Split('|');
                    if (parts.Length == 3)
                    {
                        string mac = parts[0];
                        if (_macToPlayer.TryGetValue(mac, out var idx))
                        {
                            Dispatcher.BeginInvoke(new Action(() => OnButtonPressed(idx)));
                        }
                        else
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                                SetStatus($"Неизвестный MAC: {mac} (нет в {CONFIG_FILE})")));
                        }
                    }
                }
                catch (ObjectDisposedException) { return; }
                catch (SocketException) { continue; }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(new Action(() => SetStatus("UDP error: " + ex.Message)));
                }
            }
        }

        private void TrySendAck(IPEndPoint remote, string original)
        {
            try
            {
                string ack;
                if (original.StartsWith("HB|")) ack = "ACK:HB";
                else
                {
                    var p = original.Split('|');
                    ack = p.Length == 3 ? "ACK:" + p[1] : "ACK:?";
                }
                var bytes = Encoding.ASCII.GetBytes(ack);
                _udp.Send(bytes, bytes.Length, new IPEndPoint(remote.Address, UDP_CMD_PORT));
            }
            catch { }
        }

        // ============================================================
        // Нажатие кнопки игроком
        // ============================================================
        private void OnButtonPressed(int idx)
        {
            // Игрок уже нажимал в этом раунде — игнорируем
            if (_alreadyPressedThisRound.Contains(idx))
            {
                SetStatus($"{_players[idx].NameBox.Text} уже нажимал в этом раунде — игнор.");
                return;
            }

            // Уже кто-то нажимал и ждём решения ведущего — тоже игнор
            // (пока ведущий не нажмёт «Продолжить» или «Верно»)
            if (_currentPressed != -1)
            {
                SetStatus($"{_players[idx].NameBox.Text} нажал, но сейчас отвечает " +
                          $"{_players[_currentPressed].NameBox.Text} — ждём решения ведущего.");
                return;
            }

            // Запоминаем первого нажавшего
            _currentPressed = idx;
            _alreadyPressedThisRound.Add(idx);

            // Подсвечиваем активного
            HighlightPlayer(idx, active: true);

            // Ставим музыку на паузу, запоминаем позицию
            if (_output != null && _output.PlaybackState == PlaybackState.Playing)
            {
                if (_reader != null) _pausedPosition = _reader.CurrentTime;
                _output.Pause();
                _resumeAfterContinue = true;
            }

            SetStatus($"Отвечает: {_players[idx].NameBox.Text}. " +
                      "Ведущий: «Верно» или «Продолжить».");
        }

        /// <summary>
        /// Подсветка игрока: активный (жёлтый) или отпавший (серый с крестиком).
        /// </summary>
        private void HighlightPlayer(int idx, bool active)
        {
            if (active)
            {
                _players[idx].Card.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x80));
                _players[idx].Card.BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0x88, 0x00));
            }
        }

        /// <summary>
        /// Помечаем игрока как «отпавшего в этом раунде».
        /// Карточка становится серой, в имени добавляется ✗.
        /// </summary>
        private void MarkPlayerAsDroppedOut(int idx)
        {
            _players[idx].Card.Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
            _players[idx].Card.BorderBrush = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
            _players[idx].Card.Opacity = 0.6;
        }

        /// <summary>
        /// Сброс состояния раунда — при новом раунде или остановке.
        /// </summary>
        private void EndRoundCleanup()
        {
            _currentPressed = -1;
            _alreadyPressedThisRound.Clear();
            _pausedPosition = TimeSpan.Zero;
            _resumeAfterContinue = false;

            for (int i = 0; i < 3; i++)
            {
                _players[i].Card.Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));
                _players[i].Card.BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
                _players[i].Card.Opacity = 1.0;
            }
        }

        private void BtnRoundCorrect_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPressed == -1)
            {
                SetStatus("Никто не нажимал — некому засчитывать.");
                return;
            }

            int idx = _currentPressed;

            // Начисляем балл
            ChangeScore(idx, +1);

            // Останавливаем музыку
            StopMusic();

            // Очищаем состояние раунда
            EndRoundCleanup();

            SetStatus($"{_players[idx].NameBox.Text} ответил правильно! Раунд завершён.");
        }
        private void BtnContinueRound_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPressed == -1)
            {
                SetStatus("Никто не нажимал — нечего продолжать.");
                return;
            }

            int idx = _currentPressed;

            // Помечаем игрока как «отпавшего» в этом раунде
            MarkPlayerAsDroppedOut(idx);

            _currentPressed = -1;

            // Снимаем паузу, продолжаем с той же позиции
            if (_resumeAfterContinue && _output != null && _reader != null)
            {
                _reader.CurrentTime = _pausedPosition;
                _output.Play();
                _resumeAfterContinue = false;
            }

            SetStatus($"{_players[idx].NameBox.Text} ответил неверно. " +
                      "Играем дальше — могут нажимать остальные.");
        }


        // ============================================================
        // Папка с музыкой
        // ============================================================
        private void BtnPickFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Выберите папку с музыкой (MP3/WAV)"
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            _allTracks.Clear();
            var exts = new[] { ".mp3", ".wav", ".wma", ".m4a", ".aac", ".ogg", ".flac" };
            try
            {
                foreach (var f in Directory.EnumerateFiles(dlg.SelectedPath, "*.*", SearchOption.AllDirectories))
                {
                    if (exts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()))
                        _allTracks.Add(f);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка чтения папки: " + ex.Message);
                return;
            }

            Reshuffle();

            TxtFolder.Text = $"{dlg.SelectedPath}  ({TracksTotal} файлов)";
            UpdateTracksCounter();
            SetStatus($"Загружено треков: {TracksTotal}. Осталось в текущем круге: {TracksLeft}");
        }

        // ============================================================
        // Перемешивание и счётчики
        // ============================================================
        private void Reshuffle()
        {
            _remainingTracks.Clear();
            var shuffled = _allTracks.OrderBy(_ => _rnd.Next()).ToList();
            foreach (var t in shuffled)
                _remainingTracks.Enqueue(t);
        }

        private void UpdateTracksCounter()
        {
            TxtTracksLeft.Text = $"🎵 Осталось: {TracksLeft} из {TracksTotal}";
        }

        // ============================================================
        // Новый раунд
        // ============================================================
        private void BtnNewRound_Click(object sender, RoutedEventArgs e)
        {
            if (_allTracks.Count == 0)
            {
                MessageBox.Show("Сначала выберите папку с музыкой.",
                    "Нет треков", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_remainingTracks.Count == 0)
            {
                Reshuffle();
                SetStatus("Круг закончился — треки перемешаны заново.");
            }

            EndRoundCleanup();  // сбрасываем подсветку, «отпавших» и флаги

            _currentTrackPath = _remainingTracks.Dequeue();
            UpdateTracksCounter();
            TxtNowPlaying.Text = "Сейчас: ???";

            StopMusic();

            try
            {
                _reader = new AudioFileReader(_currentTrackPath);
                _output = new WaveOutEvent { DesiredLatency = 150 };
                _output.Init(_reader);
                _output.Play();

                SliderPosition.Maximum = _reader.TotalTime.TotalSeconds;
                SliderPosition.Value = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось воспроизвести файл:\n" + ex.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetStatus($"Раунд начался! Осталось треков: {TracksLeft}");
        }

        // ============================================================
        // Плеер
        // ============================================================
        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (_output == null) return;
            if (_output.PlaybackState == PlaybackState.Paused)
                _output.Play();
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            _output?.Pause();
        }

        private void BtnResume_Click(object sender, RoutedEventArgs e)
        {
            if (_output?.PlaybackState == PlaybackState.Paused)
                _output.Play();
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            StopMusic();
            //ResetFirstPressed();
            SetStatus("Остановлено.");
        }

        private void BtnShowAnswer_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTrackPath == null) return;

            string name = System.IO.Path.GetFileNameWithoutExtension(_currentTrackPath);
            TxtNowPlaying.Text = "Ответ: " + name;

            var w = new AnswerWindow(name)
            {
                Owner = this
            };
            w.ShowDialog();
        }

        private void StopMusic()
        {
            try { _output?.Stop(); } catch { }
            try { _output?.Dispose(); } catch { }
            _output = null;

            try { _reader?.Dispose(); } catch { }
            _reader = null;

            SliderPosition.Value = 0;
            TxtPosition.Text = "0:00 / 0:00";
        }

        private void UpdatePosition()
        {
            if (_reader == null) return;
            if (_sliderDragging) return;

            var pos = _reader.CurrentTime.TotalSeconds;
            SliderPosition.Value = Math.Min(pos, SliderPosition.Maximum);
            TxtPosition.Text = $"{FormatTime(_reader.CurrentTime)} / {FormatTime(_reader.TotalTime)}";
        }

        private static string FormatTime(TimeSpan t) =>
            $"{(int)t.TotalMinutes}:{t.Seconds:D2}";

        private void SliderPosition_DragStarted(object sender,
            System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            _sliderDragging = true;
            _sliderWasPlaying = _output?.PlaybackState == PlaybackState.Playing;
            _output?.Pause();
        }

        private void SliderPosition_DragCompleted(object sender,
            System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (_reader != null)
                _reader.CurrentTime = TimeSpan.FromSeconds(SliderPosition.Value);
            _sliderDragging = false;
            if (_sliderWasPlaying) _output?.Play();
        }

        private void SliderPosition_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_sliderDragging) return;
            if (_reader != null)
                TxtPosition.Text =
                    $"{FormatTime(TimeSpan.FromSeconds(e.NewValue))} / {FormatTime(_reader.TotalTime)}";
        }

        // ============================================================
        // Утилиты
        // ============================================================
        private void SetStatus(string text)
        {
            TxtStatus.Text = text;
        }

        // ============================================================
        // Внутренний класс "Игрок"
        // ============================================================
        private class Player
        {
            public string Name;
            public string Mac;
            public int Score;

            public TextBox NameBox;
            public TextBlock ScoreBox;
            public Border Card;
        }
    }
}
