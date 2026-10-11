using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GuessMelody.Core.Models
{
    public class SettingsPreset
    {
        public string Name { get; set; } = "default";
        public GameRules Rules { get; set; } = new GameRules();
        public AudioSettings Audio { get; set; } = new AudioSettings();
        public TimingSettings Timings { get; set; } = new TimingSettings();
        public AppearanceSettings Appearance { get; set; } = new AppearanceSettings();
    }
    public class GameRules
    {
        public int PlayerCount { get; set; } = 3;
        public int RoundCount { get; set; } = 5;              // 0 = неограниченно
        public int FragmentSec { get; set; } = 20;
        public int DefaultStartSec { get; set; } = 0;
        public bool RandomStart { get; set; } = false;
        public bool CountdownOnAnswer { get; set; } = true;
        public bool IsSetupMode { get; set; } = false;
        public bool AllowRePressAfterWrong { get; set; } = false;
        public int ScorePerCorrect { get; set; } = 1;
    }
    public class Team
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Mac { get; set; } = "";
        public int Score { get; set; }

        // ---- Heartbeat-состояние (runtime, не сериализуется) ----
        [Newtonsoft.Json.JsonIgnore]
        public DateTime LastHeartbeatUtc { get; set; } = DateTime.MinValue;

        [Newtonsoft.Json.JsonIgnore]
        public bool IsAlive { get; set; }

        [Newtonsoft.Json.JsonIgnore]
        public long HeartbeatCount { get; set; }

        /// <summary>Сколько секунд назад был последний HB. Если ни разу — большое число.</summary>
        public double SecondsSinceHeartbeat =>
            LastHeartbeatUtc == DateTime.MinValue
                ? double.MaxValue
                : (DateTime.UtcNow - LastHeartbeatUtc).TotalSeconds;

        public Team Clone() => new Team
        {
            Id = Id,
            Name = Name,
            Mac = Mac,
            Score = Score
        };

        public override string ToString() => $"{Name} ({Mac}) [{Score}]";
    }


    public class Track
    {
        public string RelativePath { get; set; } = "";
        public double DurationSec { get; set; }
        public long FileSizeBytes { get; set; }
        public bool IsUnsupported { get; set; }
        public TrackOverrides Overrides { get; set; }   // null = использовать defaults

        public bool HasOverrides =>
            Overrides != null &&
            (Overrides.StartSec != null || Overrides.DurationSec != null ||
             Overrides.Volume != null || Overrides.Loop != null ||
             Overrides.RandomStart != null);
    }

    public class TrackOverrides
    {
        public double? StartSec { get; set; }
        public double? DurationSec { get; set; }
        public int? Volume { get; set; }
        public bool? Loop { get; set; }
        public bool? RandomStart { get; set; }

        public TrackOverrides Clone() => new TrackOverrides
        {
            StartSec = StartSec,
            DurationSec = DurationSec,
            Volume = Volume,
            Loop = Loop,
            RandomStart = RandomStart
        };
    }

    public class Category
    {
        public string Name { get; set; } = "";
        public List<Track> Tracks { get; set; } = new List<Track>();

        public Category Clone() => new Category
        {
            Name = Name,
            Tracks = new List<Track>(Tracks)
        };
    }

    public class TimingSettings
    {
        public int CountdownBeforeSec { get; set; } = 3;
        public int AnswerTimeoutSec { get; set; } = 30;       // 0 = без ограничения
        public int PauseAfterPressMs { get; set; } = 0;
        public int PauseBetweenRoundsSec { get; set; } = 3;
        public bool AutoNextRound { get; set; } = false;

        public int HeartbeatTimeoutSec { get; set; } = 11;
        

        private int _wrongPause = 3;
        public int WrongFeedbackPauseSec
        {
            get => _wrongPause;
            set => _wrongPause = value;
        }
    }

    public class AudioSettings
    {
        public int Volume { get; set; } = 80;                 // 0..100
        public string BuzzerPath { get; set; } = "";
        public string CorrectPath { get; set; } = "";
        public string WrongPath { get; set; } = "";
        public string TickPath { get; set; } = "";
        public int FadeInMs { get; set; } = 100;
        public int FadeOutMs { get; set; } = 100;
        public bool TickLastFiveSeconds { get; set; } = true;
    }

    public class AppearanceSettings
    {
        public string PlayerBgColor { get; set; } = "#1E1E2E";
        public string PlayerAccentColor { get; set; } = "#FFD166";
        public string PlayerTextColor { get; set; } = "#EEEEEE";
        public int CategoryFontSize { get; set; } = 32;
        public int PlayerNameFontSize { get; set; } = 28;
        public int PlayerScoreFontSize { get; set; } = 40;

        // Главное окно
        public string AppTheme { get; set; } = "Light";       // Light / Dark
        public int LogFontSize { get; set; } = 12;
    }

    public class ButtonsMap
    {
        public List<Team> Teams { get; set; } = new List<Team>();
    }



    public class AnswerServerSettings
    {
        public int Port { get; set; } = 8080;
        public string PageTitle { get; set; } = "Угадай мелодию";
        public string PlaceholderText { get; set; } = "";
    }
    public class FoldersCatalog
    {
        public string RootFolder { get; set; } = "";
        public string PresetName { get; set; } = "default";
        public TrackOverrides Defaults { get; set; } = new TrackOverrides
        {
            StartSec = 0,
            DurationSec = 20,
            Volume = 80,
            Loop = false,
            RandomStart = false
        };
        public List<Category> Categories { get; set; } = new List<Category>();
    }

    public class GameSettings
    {
        public int Version { get; set; } = 1;
        public string Name { get; set; } = "Новая игра";
        public string CreatedUtc { get; set; } = "";

        public GameRules Rules { get; set; } = new GameRules();
        public AudioSettings Audio { get; set; } = new AudioSettings();
        public TimingSettings Timings { get; set; } = new TimingSettings();
        public AppearanceSettings Appearance { get; set; } = new AppearanceSettings();
        public ButtonsMap Buttons { get; set; } = new ButtonsMap();
        public FoldersCatalog Folders { get; set; } = new FoldersCatalog();
        public AnswerServerSettings AnswerServer { get; set; } = new AnswerServerSettings();
    }

}
