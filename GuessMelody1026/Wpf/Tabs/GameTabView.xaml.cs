using GuessMelody.Core.Audio;
using GuessMelody.Core.Models;
using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace GuessMelody.Wpf.Tabs
{
    public partial class GameTabView : UserControl
    {
        private NaAudioEngine _audio => AppServices.Audio;

        public GameTabView()
        {
            InitializeComponent();
            DataContext = AppServices.GameSettings;
            Loaded += (_, __) => UpdatePathText();
        }

        private void UpdatePathText()
        {
            try
            {
                PathText.Text = "Файл: " +
                    Path.GetFullPath(AppServices.GameSettingsPath);
            }
            catch { PathText.Text = ""; }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppServices.SaveGameSettings();
                PathText.Text = $"Сохранено: {Path.GetFullPath(AppServices.GameSettingsPath)}";
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
                var loaded = GuessMelody.Core.Storage.JsonStore
                    .Load<GameSettings>(AppServices.GameSettingsPath);
                // Копируем значения в существующий объект, чтобы не рвать ссылки
                CopySettings(loaded, AppServices.GameSettings);
                DataContext = AppServices.GameSettings;
                UpdatePathText();
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
                    MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            CopySettings(new GameSettings(), AppServices.GameSettings);
            UpdatePathText();
        }

        private static void CopySettings(GameSettings src, GameSettings dst)
        {
            // Простая ручная копия — быстрее и надёжнее рефлексии.
            dst.PlayDurationSec = src.PlayDurationSec;
            dst.StartAtMode = src.StartAtMode;
            dst.StartAtSec = src.StartAtSec;
            dst.PlayRoundStartSound = src.PlayRoundStartSound;
            dst.RoundStartSoundFile = src.RoundStartSoundFile;
            dst.PlayCountdown = src.PlayCountdown;
            dst.CountdownSeconds = src.CountdownSeconds;
            dst.CountdownTickSoundFile = src.CountdownTickSoundFile;
            dst.CountdownEndSoundFile = src.CountdownEndSoundFile;
            dst.AnswerSeconds = src.AnswerSeconds;
            dst.AnswerTickSoundFile = src.AnswerTickSoundFile;
            dst.RightSoundFile = src.RightSoundFile;
            dst.WrongSoundFile = src.WrongSoundFile;
            dst.AutoNextRound = src.AutoNextRound;
            dst.MaxRounds = src.MaxRounds;
            dst.GridColumns = src.GridColumns;
            dst.GridRows = src.GridRows;
        }

        // -------- Выбор файлов --------
        private void PickRoundStart_Click(object o, RoutedEventArgs e) =>
            PickSound(p => AppServices.GameSettings.RoundStartSoundFile = p);
        private void PickTick_Click(object o, RoutedEventArgs e) =>
            PickSound(p => AppServices.GameSettings.CountdownTickSoundFile = p);
        private void PickEnd_Click(object o, RoutedEventArgs e) =>
            PickSound(p => AppServices.GameSettings.CountdownEndSoundFile = p);
        private void PickAnswerTick_Click(object o, RoutedEventArgs e) =>
            PickSound(p => AppServices.GameSettings.AnswerTickSoundFile = p);
        private void PickRight_Click(object o, RoutedEventArgs e) =>
            PickSound(p => AppServices.GameSettings.RightSoundFile = p);
        private void PickWrong_Click(object o, RoutedEventArgs e) =>
            PickSound(p => AppServices.GameSettings.WrongSoundFile = p);

        private void PickSound(Action<string> assign)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Аудио (*.mp3;*.wav;*.ogg;*.aiff;*.m4a;*.flac)|*.mp3;*.wav;*.ogg;*.aiff;*.m4a;*.flac|Все файлы|*.*"
            };
            if (dlg.ShowDialog() == true) assign(dlg.FileName);
        }

        // -------- Тест звука --------
        private void TestRoundStart_Click(object o, RoutedEventArgs e) =>
            TestSound(AppServices.GameSettings.RoundStartSoundFile);
        private void TestTick_Click(object o, RoutedEventArgs e) =>
            TestSound(AppServices.GameSettings.CountdownTickSoundFile);
        private void TestEnd_Click(object o, RoutedEventArgs e) =>
            TestSound(AppServices.GameSettings.CountdownEndSoundFile);
        private void TestAnswerTick_Click(object o, RoutedEventArgs e) =>
            TestSound(AppServices.GameSettings.AnswerTickSoundFile);
        private void TestRight_Click(object o, RoutedEventArgs e) =>
            TestSound(AppServices.GameSettings.RightSoundFile);
        private void TestWrong_Click(object o, RoutedEventArgs e) =>
            TestSound(AppServices.GameSettings.WrongSoundFile);

        private void TestSound(string file)
        {
            if (string.IsNullOrWhiteSpace(file)) return;
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
                MessageBox.Show(ex.Message, "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}