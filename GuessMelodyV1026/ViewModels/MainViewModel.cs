using System.Windows.Input;
using GuessMelody.Services;

namespace GuessMelody.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        public GameTabViewModel Game { get; }
        public SettingsTabViewModel Settings { get; }
        public ButtonsTabViewModel Buttons { get; }
        public FoldersTabViewModel Folders { get; }
        public LogTabViewModel Log { get; }
        public AnswerServerTabViewModel AnswerServer { get; }

        public ICommand ExitCommand { get; }

        public MainViewModel(
            GameTabViewModel game,
            SettingsTabViewModel settings,
            ButtonsTabViewModel buttons,
            FoldersTabViewModel folders,
            LogTabViewModel log,
            AnswerServerTabViewModel answerServer)
        {
            Game = game;
            Settings = settings;
            Buttons = buttons;
            Folders = folders;
            Log = log;
            AnswerServer = answerServer;

            // Связываем GameTab с соседними VM — ему нужны ссылки для load/save
            Game.Buttons = Buttons;
            Game.Settings = settings;
            Game.Folders = folders;

            // Синхронизировать привязки кнопок при старте: если уже есть команды — разложить их
            SyncStartupBindings();

            ExitCommand = new RelayCommand(_ =>
            {
                SaveAutosave();
                System.Windows.Application.Current.Shutdown();
            });
        }

        private void SyncStartupBindings()
        {
            // если в GameSettings уже есть Teams (например, после автозагрузки) —
            // разложим их по вкладке «Кнопки»
            //var teams = Game.Buttons != null && Game != null ? null : null; // placeholder
            // используем методы GameTab
            // На старте просто ничего не делаем — LoadGame сам всё синхронизирует
        }

        private void SaveAutosave()
        {
            try
            {
                var dir = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "GuessMelody");
                System.IO.Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, "autosave.gmgame");

                var s = Game.GetSettingsSnapshotForAutosave();
                GuessMelody.Core.Serialization.JsonStore.Save(path, s);
            }
            catch { }
        }

        private string _statusText = "Готово";
        public string StatusText
        {
            get => _statusText;
            set => Set(ref _statusText, value);
        }
    }
}