using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuessMelody.Core.Models;

namespace GuessMelody.Core.Game
{
    /// <summary>
    /// Сканирование папки игры, категорий и треков.
    /// Хранит и применяет настройки превью.
    /// </summary>
    public sealed class FolderManager
    {
        // Какие расширения считаем аудио
        public static readonly HashSet<string> SupportedExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp3", ".wav", ".ogg", ".aiff", ".aif", ".m4a", ".flac"
            };

        private readonly FolderConfig _config;

        public FolderConfig Config => _config;

        public FolderManager(FolderConfig config)
        {
            _config = config ?? new FolderConfig();
            ResolveFullPaths();
        }

        // =============================================================
        // Сканирование
        // =============================================================

        /// <summary>
        /// Установить корневую папку и пересканировать категории.
        /// Имена категорий берём из названий подпапок, если они ещё не заданы.
        /// </summary>
        public void SetRootAndScan(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
                throw new ArgumentException("Пустой путь", nameof(rootPath));

            if (!Directory.Exists(rootPath))
                throw new DirectoryNotFoundException(rootPath);

            _config.RootPath = rootPath;
            Scan();
        }

        /// <summary>
        /// Пересканировать корневую папку. Сохраняем уже заданные пользователем имена
        /// и персональные настройки превью.
        /// </summary>
        public void Scan()
        {
            if (string.IsNullOrWhiteSpace(_config.RootPath) ||
                !Directory.Exists(_config.RootPath))
                return;

            // Снимок старых имён категорий по RelativePath — чтобы не потерять
            // переименования, сделанные пользователем.
            var oldNames = _config.Categories
                .Where(c => !string.IsNullOrEmpty(c.RelativePath))
                .ToDictionary(c => c.RelativePath, c => c.Name, StringComparer.OrdinalIgnoreCase);

            var categories = new List<CategoryConfig>();

            foreach (var dir in Directory.GetDirectories(_config.RootPath))
            {
                var rel = dir.StartsWith(_config.RootPath, StringComparison.OrdinalIgnoreCase)
                    ? dir.Substring(_config.RootPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    : dir;
                var name = oldNames.TryGetValue(rel, out var saved)
                    ? saved
                    : Path.GetFileName(dir);

                var cat = new CategoryConfig
                {
                    Name = name,
                    RelativePath = rel.Replace('\\', '/'),
                    FullPath = dir,
                    Tracks = ScanTracks(dir)
                };

                categories.Add(cat);
            }

            categories.Sort((a, b) => string.Compare(a.Name, b.Name,
                                                     StringComparison.CurrentCultureIgnoreCase));
            _config.Categories = categories;
        }

        /// <summary>
        /// Сканировать один каталог на аудиофайлы (без рекурсии).
        /// </summary>
        public static List<string> ScanTracks(string dir)
        {
            if (!Directory.Exists(dir)) return new List<string>();

            return Directory.EnumerateFiles(dir)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
                .Select(Path.GetFileName)
                .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private void ResolveFullPaths()
        {
            if (string.IsNullOrWhiteSpace(_config.RootPath)) return;

            foreach (var c in _config.Categories)
            {
                if (!string.IsNullOrEmpty(c.RelativePath))
                    c.FullPath = Path.Combine(_config.RootPath,
                        c.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            }
        }

        // =============================================================
        // Доступ к данным
        // =============================================================

        public IReadOnlyList<CategoryConfig> Categories => _config.Categories;

        public CategoryConfig GetCategory(int index) =>
            (index >= 0 && index < _config.Categories.Count)
                ? _config.Categories[index]
                : null;

        public string GetFullTrackPath(CategoryConfig cat, string trackName)
        {
            if (cat == null || string.IsNullOrEmpty(trackName)) return null;
            string dir = string.IsNullOrEmpty(cat.FullPath)
                ? Path.Combine(_config.RootPath,
                    cat.RelativePath?.Replace('/', Path.DirectorySeparatorChar) ?? "")
                : cat.FullPath;
            return Path.Combine(dir, trackName);
        }

        /// <summary>
        /// Случайный трек из пула (можно передать свой пул из RoundEngine).
        /// </summary>
        public string GetRandomTrack(CategoryConfig cat, ISet<string> exclude = null, Random rng = null)
        {
            if (cat == null || cat.Tracks == null || cat.Tracks.Count == 0) return null;
            if (rng == null)
                rng = new Random();

            var pool = exclude == null
                ? cat.Tracks
                : cat.Tracks.Where(t => !exclude.Contains(t)).ToList();

            if (pool.Count == 0) return null;
            return pool[rng.Next(pool.Count)];
        }

        // =============================================================
        // Превью
        // =============================================================

        /// <summary>
        /// Получить стартовую секунду превью для трека:
        ///   - если настроено per-track → вернуть её,
        ///   - иначе — DefaultPreviewStartSec.
        /// </summary>
        public double GetPreviewStartSec(CategoryConfig cat, string trackName)
        {
            if (cat == null || string.IsNullOrEmpty(trackName))
                return _config.DefaultPreviewStartSec;

            var key = _config.Key(cat, trackName);
            return _config.PreviewStartSec.TryGetValue(key, out var sec)
                ? sec
                : _config.DefaultPreviewStartSec;
        }

        public void SetPreviewStartSec(CategoryConfig cat, string trackName, double sec)
        {
            if (cat == null || string.IsNullOrEmpty(trackName)) return;
            if (sec < 0) sec = 0;
            _config.PreviewStartSec[_config.Key(cat, trackName)] = sec;
        }

        public void ClearPreviewStartSec(CategoryConfig cat, string trackName)
        {
            if (cat == null || string.IsNullOrEmpty(trackName)) return;
            _config.PreviewStartSec.Remove(_config.Key(cat, trackName));
        }

        public void RefreshPaths()
        {
            ResolveFullPaths();
        }

        // =============================================================
        // Утилиты
        // =============================================================

        public int TotalCategories => _config.Categories?.Count ?? 0;

        public int TotalTracks =>
            _config.Categories?.Sum(c => c.Tracks?.Count ?? 0) ?? 0;

        /// <summary>Есть ли хоть один трек во всех категориях.</summary>
        public bool HasAnyTracks() => TotalTracks > 0;
    }
}