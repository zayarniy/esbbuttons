using GuessMelody.Core.Audio;
using GuessMelody.Core.Game;
using GuessMelody.Core.Models;
using GuessMelody.Core.Storage;
using GuessMelody.Wpf.Game;
using GuessMelody.Wpf.Logging;
using System;


namespace GuessMelody.Wpf
{
    /// <summary>
    /// Общие сервисы приложения. Создаются один раз, живут до закрытия.
    /// </summary>
    public static class AppServices
    {
        // --- Пути к JSON ---
        public static string GameSettingsPath { get; } = "game.json";
        public static string ButtonBindingsPath { get; } = "buttons.json";
        public static string FolderConfigPath { get; private set; } = "folders.json";

        // --- Настройки / конфиги ---
        public static GameSettings GameSettings { get; private set; }
        public static ButtonBindings ButtonBindings { get; private set; }
        public static FolderConfig FolderConfig { get; private set; }

        // --- Сервисы ---
        public static EspButtonHub Hub { get; private set; }
        public static ButtonService ButtonService { get; private set; }
        public static FolderManager FolderManager { get; private set; }
        public static NaAudioEngine Audio { get; private set; }
        

        // --- Состояние сервера ---
        public static bool IsServerRunning => Hub != null;
        public static string CurrentPresetPath { get; private set; } = null;
        public static void SetCurrentPresetPath(string path) => CurrentPresetPath = path;
        public static void Initialize()
        {
            // Загружаем конфиги (или создаём дефолтные)
            GameSettings = JsonStore.Load<GameSettings>(GameSettingsPath);
            ButtonBindings = JsonStore.Load<ButtonBindings>(ButtonBindingsPath);
            FolderConfig = JsonStore.Load<FolderConfig>(FolderConfigPath);

            FolderManager = new FolderManager(FolderConfig);
            if (!string.IsNullOrWhiteSpace(FolderConfig.RootPath) &&
                System.IO.Directory.Exists(FolderConfig.RootPath))
            {
                try { 
                    FolderManager.Scan();
                } catch { /* ignore */ }
            }

            Audio = new NaAudioEngine();
        
    // ButtonService пока null — подключим позже в StartServer.
         Game.GameController.Instance.Initialize(Audio, FolderManager, null, GameSettings);
        }

        /// <summary>
        /// Запустить UDP-сервер на указанном порту.
        /// </summary>
        public static void StartServer(int port)
        {
            if (IsServerRunning) return;
           // GameController.Instance.ResetEngine();
            AppLogger.Instance.Info($"Запуск UDP-сервера на порту {port}");
            Hub = new EspButtonHub(port, port);
            ButtonService = new ButtonService(Hub);
            ButtonService.ApplyBindings(ButtonBindings.Bindings);

            Hub.Start();
            GameController.Instance.Initialize(Audio, FolderManager, ButtonService, GameSettings);
            AppLogger.Instance.Info("Сервер запущен.");
        }



        public static void SetFolderConfigPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            FolderConfigPath = path;
        }

        public static void StopServer()
        {
            AppLogger.Instance.Info("Остановка сервера.");
            try { ButtonService?.Dispose(); } catch { }
            try { Hub?.Dispose(); } catch { }
            ButtonService = null;
            Hub = null;
        }

        public static void SaveGameSettings()
        {
            JsonStore.Save(GameSettingsPath, GameSettings);
        }

        public static void SaveButtonBindings()
        {
            if (ButtonService != null)
                ButtonBindings = ButtonService.ExportBindings();
            JsonStore.Save(ButtonBindingsPath, ButtonBindings);
        }

        public static void SaveFolderConfig()
        {
            JsonStore.Save(FolderConfigPath, FolderConfig);
        }

        public static void Shutdown()
        {
            try { Audio?.Dispose(); } catch { }
            StopServer();
        }
    }
}