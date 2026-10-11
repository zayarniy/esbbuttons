using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuizGame.QuizGame.Core
{
    public class FoldersSettings
{
    // Корневая папка игры. Внутри — подпапки-категории.
    public string RootFolder { get; set; } = "";

    // Список категорий с настройками
    public List<CategoryInfo> Categories { get; set; } = new List<CategoryInfo>();
}

public class CategoryInfo
{
    public string Name { get; set; }             // имя подпапки
    public string Path { get; set; }             // полный путь
    public List<TrackInfo> Tracks { get; set; } = new List<TrackInfo>();
}

public class TrackInfo
{
    public string FileName { get; set; }
    public string FullPath { get; set; }
    public double PreviewStartSec { get; set; }  // с какого места слушать в настройках
    public double DurationSec { get; set; }      // длительность (кэш)
}
}
