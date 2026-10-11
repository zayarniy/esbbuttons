using System;

namespace GuessMelody.Core.Models
{
    /// <summary>
    /// Полный снимок конфигурации приложения.
    /// Сохраняется одним файлом, загружается одним кликом.
    /// </summary>
    public class GamePreset
    {
        public int SchemaVersion { get; set; } = 1;
        public string Name { get; set; } = "";
        public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;

        public GameSettings Game { get; set; } = new GameSettings();
        public ButtonBindings Buttons { get; set; } = new ButtonBindings();
        public FolderConfig Folders { get; set; } = new FolderConfig();
    }
}