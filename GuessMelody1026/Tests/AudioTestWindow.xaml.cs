using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using GuessMelody.Core.Audio;
using Microsoft.Win32;

namespace GuessMelody.Wpf
{
    public partial class AudioTestWindow : Window
    {
        private readonly NaAudioEngine _engine = new NaAudioEngine();
        private readonly DispatcherTimer _uiTimer;

        private string _oneShotFile;
        private bool _isDraggingSlider;

        public AudioTestWindow()
        {
            InitializeComponent();

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _uiTimer.Tick += (_, __) => UpdateUI();
            _uiTimer.Start();

            _engine.TrackEnded += (_, __) =>
                Dispatcher.BeginInvoke(new Action(() => Log("Track ended")));

            Log("Готов. Откройте mp3/wav/ogg файл.");

            Closed += (_, __) =>
            {
                _uiTimer.Stop();
                _engine.Dispose();
            };
        }

        // =============================================================
        // Файл
        // =============================================================
        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Аудио (*.mp3;*.wav;*.ogg;*.aiff;*.m4a)|*.mp3;*.wav;*.ogg;*.aiff;*.m4a|Все файлы|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                _engine.Load(dlg.FileName);
                FileBox.Text = dlg.FileName;
                Log($"Загружено: {Path.GetFileName(dlg.FileName)}  " +
                    $"(Duration={_engine.Duration:mm\\:ss\\.fff})");
            }
            catch (Exception ex)
            {
                Log("Ошибка загрузки: " + ex.Message);
            }
        }

        // =============================================================
        // Управление
        // =============================================================
        private void Play_Click(object sender, RoutedEventArgs e)
        {
            try { _engine.Play(); Log("Play"); }
            catch (Exception ex) { Log("Play error: " + ex.Message); }
        }

        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            try { _engine.Pause(); Log("Pause"); }
            catch (Exception ex) { Log("Pause error: " + ex.Message); }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            try { _engine.Stop(); Log("Stop"); }
            catch (Exception ex) { Log("Stop error: " + ex.Message); }
        }

        private void Back10_Click(object sender, RoutedEventArgs e)
        {
            _engine.Seek(_engine.Position - TimeSpan.FromSeconds(10));
            Log($"Seek → {_engine.Position:mm\\:ss\\.fff}");
        }

        private void Fwd10_Click(object sender, RoutedEventArgs e)
        {
            _engine.Seek(_engine.Position + TimeSpan.FromSeconds(10));
            Log($"Seek → {_engine.Position:mm\\:ss\\.fff}");
        }

        // =============================================================
        // Позиция
        // =============================================================
        private void PosSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;
            // Клик по слайдеру (не перетаскивание) — перескочить
            if (System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                _isDraggingSlider = true;
                SeekFromSlider();
            }
            else if (_isDraggingSlider)
            {
                _isDraggingSlider = false;
                SeekFromSlider();
            }
        }

        private void SeekFromSlider()
        {
            var dur = _engine.Duration;
            if (dur <= TimeSpan.Zero) return;
            var pos = TimeSpan.FromSeconds(dur.TotalSeconds * (PosSlider.Value / PosSlider.Maximum));
            _engine.Seek(pos);
            Log($"Seek → {pos:mm\\:ss\\.fff}");
        }

        // =============================================================
        // Громкость
        // =============================================================
        private void VolSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_engine == null) return;
            _engine.Volume = (float)VolSlider.Value;
            if (VolText != null)
                VolText.Text = $"{_engine.Volume:P0}";
        }

        // =============================================================
        // One-shot
        // =============================================================
        private void OpenOneShot_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Аудио (*.mp3;*.wav;*.ogg)|*.mp3;*.wav;*.ogg|Все файлы|*.*"
            };
            if (dlg.ShowDialog() != true) return;
            _oneShotFile = dlg.FileName;
            _engine.PlayOneShot(_oneShotFile);
            Log($"OneShot: {Path.GetFileName(_oneShotFile)}");
        }

        private void PlayTick_Click(object sender, RoutedEventArgs e) =>
            PlayOneShotRelative("tick.wav");

        private void PlayRight_Click(object sender, RoutedEventArgs e) =>
            PlayOneShotRelative("right.wav");

        private void PlayWrong_Click(object sender, RoutedEventArgs e) =>
            PlayOneShotRelative("wrong.wav");

        private void PlayOneShotRelative(string name)
        {
            // Ищем рядом с exe, в подпапке sounds
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sounds", name);
            if (!File.Exists(path))
            {
                Log($"Нет файла: {path}");
                return;
            }
            _engine.PlayOneShot(path);
            Log($"OneShot: {name}");
        }

        // =============================================================
        // Обновление UI
        // =============================================================
        private void UpdateUI()
        {
            var pos = _engine.Position;
            var dur = _engine.Duration;
            TimeText.Text = $"{pos:mm\\:ss\\.fff} / {dur:mm\\:ss\\.fff}";
            StateText.Text = _engine.IsPlaying ? "▶ Playing" : "⏸ Idle";

            if (!_isDraggingSlider && dur > TimeSpan.Zero)
            {
                double ratio = pos.TotalSeconds / dur.TotalSeconds;
                Progress.Value = ratio * 1000.0;
                PosSlider.Value = ratio * 1000.0;
            }
        }

        private void Log(string s)
        {
            LogList.Items.Add($"[{DateTime.Now:HH:mm:ss.fff}] {s}");
            if (LogList.Items.Count > 500) LogList.Items.RemoveAt(0);
            LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
        }
    }
}