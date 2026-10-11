using System;
using System.IO;
using System.Windows;
using GuessMelody.Core.Audio;
using GuessMelody.Core.Models;
using GuessMelody.Core.Storage;
using Microsoft.Win32;

namespace GuessMelody.Wpf
{
    public partial class GameSettingsWindow : Window
    {
        private const string SettingsPath = "game.json";

        private GameSettings _settings;
        private readonly NaAudioEngine _audio = new NaAudioEngine();

        public GameSettingsWindow()
        {
            InitializeComponent();

            _settings = JsonStore.Load<GameSettings>(SettingsPath);
            DataContext = _settings;

            PathText.Text = "Файл: " + Path.GetFullPath(SettingsPath);

            Closed += (_, __) => _audio.Dispose();
        }

        // =============================================================
        // Сохранить / Загрузить / Сбросить
        // =============================================================
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                JsonStore.Save(SettingsPath, _settings);
                PathText.Text = $"Сохранено: {Path.GetFullPath(SettingsPath)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка сохранения",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Load_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _settings = JsonStore.Load<GameSettings>(SettingsPath);
                DataContext = _settings;
                PathText.Text = $"Загружено: {Path.GetFullPath(SettingsPath)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка загрузки",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Сбросить все настройки к дефолтным?",
                    "Подтверждение", MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            _settings = new GameSettings();
            DataContext = _settings;
            PathText.Text = "Сброшено к дефолтным (не сохранено)";
        }

        // =============================================================
        // Выбор файла звука (заполняет соответствующее поле)
        // =============================================================
        private void PickRoundStart_Click(object o, RoutedEventArgs e) =>
            PickSound(p => _settings.RoundStartSoundFile = p);

        private void PickTick_Click(object o, RoutedEventArgs e) =>
            PickSound(p => _settings.CountdownTickSoundFile = p);

        private void PickEnd_Click(object o, RoutedEventArgs e) =>
            PickSound(p => _settings.CountdownEndSoundFile = p);

        private void PickAnswerTick_Click(object o, RoutedEventArgs e) =>
            PickSound(p => _settings.AnswerTickSoundFile = p);

        private void PickRight_Click(object o, RoutedEventArgs e) =>
            PickSound(p => _settings.RightSoundFile = p);

        private void PickWrong_Click(object o, RoutedEventArgs e) =>
            PickSound(p => _settings.WrongSoundFile = p);

        private void PickSound(Action<string> assign)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Аудио (*.mp3;*.wav;*.ogg;*.aiff;*.m4a;*.flac)|*.mp3;*.wav;*.ogg;*.aiff;*.m4a;*.flac|Все файлы|*.*"
            };
            if (dlg.ShowDialog() != true) return;
            assign(dlg.FileName);
        }

        // =============================================================
        // Проигрывание тестового звука
        // =============================================================
        private void TestRoundStart_Click(object o, RoutedEventArgs e) =>
            TestSound(_settings.RoundStartSoundFile);

        private void TestTick_Click(object o, RoutedEventArgs e) =>
            TestSound(_settings.CountdownTickSoundFile);

        private void TestEnd_Click(object o, RoutedEventArgs e) =>
            TestSound(_settings.CountdownEndSoundFile);

        private void TestAnswerTick_Click(object o, RoutedEventArgs e) =>
            TestSound(_settings.AnswerTickSoundFile);

        private void TestRight_Click(object o, RoutedEventArgs e) =>
            TestSound(_settings.RightSoundFile);

        private void TestWrong_Click(object o, RoutedEventArgs e) =>
            TestSound(_settings.WrongSoundFile);

        private void TestSound(string file)
        {
            if (string.IsNullOrWhiteSpace(file))
            {
                MessageBox.Show("Путь к файлу не задан.",
                    "Тест звука", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string path = Path.IsPathRooted(file)
                ? file
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, file);

            if (!File.Exists(path))
            {
                MessageBox.Show($"Файл не найден:\n{path}",
                    "Тест звука", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try { _audio.PlayOneShot(path); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка воспроизведения",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}