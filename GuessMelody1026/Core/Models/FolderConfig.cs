using System;
using System.Collections.Generic;

namespace GuessMelody.Core.Models
{
    public class CategoryConfig
    {
        public string Name { get; set; }
        public string RelativePath { get; set; }
        public List<string> Tracks { get; set; } = new List<string>();

        // ѕолный путь к папке категории на диске Ч вычисл€етс€ менеджером.
        [Newtonsoft.Json.JsonIgnore]
        public string FullPath { get; set; }

        public int TrackCount => Tracks?.Count ?? 0;
    }

    public class FolderConfig
    {
        public int SchemaVersion { get; set; } = 1;
        public string RootPath { get; set; } = "";
        public double DefaultPreviewStartSec { get; set; } = 0.0;
        public List<CategoryConfig> Categories { get; set; } = new List<CategoryConfig>();

        public List<ButtonBinding> Bindings { get; } = new List<ButtonBinding>();

        //  люч Ч "<relativePath>/<fileName>"
        public Dictionary<string, double> PreviewStartSec { get; set; }
            = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        public string Key(CategoryConfig cat, string track) =>
            $"{cat.RelativePath?.Replace('\\', '/')}/{track}";
    }
}