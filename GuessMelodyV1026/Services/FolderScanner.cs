using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NAudio.Wave;
using GuessMelody.Core;
using GuessMelody.Core.Enums;
using GuessMelody.Core.Models;

namespace GuessMelody.Services
{
    public class FolderScanner
    {
        private static readonly string[] SupportedExt = { ".mp3", ".wav" };

        private readonly LogService _log;
        public FolderScanner(LogService log) { _log = log; }

        /// <summary>
        /// Сканирует папку и возвращает категории с треками.
        /// RelativePath — относительно rootFolder.
        /// </summary>
        public List<Category> Scan(string rootFolder, bool computeDuration = true)
        {
            var result = new List<Category>();
            if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
            {
                _log.Add(LogKind.Error, $"Папка не найдена: {rootFolder}");
                return result;
            }

            try
            {
                var subdirs = Directory.GetDirectories(rootFolder)
                    .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);

                foreach (var dir in subdirs)
                {
                    var cat = new Category { Name = Path.GetFileName(dir) };

                    var files = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(f => SupportedExt.Contains(Path.GetExtension(f).ToLowerInvariant()))
                        .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);

                    foreach (var file in files)
                    {
                        var rel = MakeRelative(rootFolder, file);
                        var track = new Track
                        {
                            RelativePath = rel,
                            FileSizeBytes = SafeSize(file),
                            IsUnsupported = false
                        };

                        if (computeDuration)
                            track.DurationSec = TryReadDuration(file);

                        cat.Tracks.Add(track);
                    }

                    // Категории с 0 треков тоже оставляем — пользователь может в них разложить позже
                    result.Add(cat);
                }

                // Файлы в корне — как псевдо-категория "(без категории)"
                var rootFiles = Directory.GetFiles(rootFolder, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => SupportedExt.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (rootFiles.Count > 0)
                {
                    var rootCat = new Category { Name = "(без категории)" };
                    foreach (var file in rootFiles)
                    {
                        var rel = MakeRelative(rootFolder, file);
                        var track = new Track
                        {
                            RelativePath = rel,
                            FileSizeBytes = SafeSize(file),
                            IsUnsupported = false
                        };
                        if (computeDuration) track.DurationSec = TryReadDuration(file);
                        rootCat.Tracks.Add(track);
                    }
                    result.Insert(0, rootCat);
                }
            }
            catch (Exception ex)
            {
                _log.Add(LogKind.Error, $"Ошибка сканирования: {ex.Message}");
            }

            _log.Add(LogKind.System,
                $"Просканировано: {result.Count} категорий, " +
                $"{result.Sum(c => c.Tracks.Count)} треков");
            return result;
        }

        private static string MakeRelative(string root, string full)
        {
            try
            {
                var rootFull = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                var fullAbs = Path.GetFullPath(full);
                if (fullAbs.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                    return fullAbs.Substring(rootFull.Length);
                return fullAbs;
            }
            catch { return full; }
        }

        private static long SafeSize(string file)
        {
            try { return new FileInfo(file).Length; } catch { return 0; }
        }

        private static double TryReadDuration(string file)
        {
            try
            {
                using (var reader = new AudioFileReader(file))
                    return reader.TotalTime.TotalSeconds;
            }
            catch
            {
                return 0;
            }
        }
    }
}