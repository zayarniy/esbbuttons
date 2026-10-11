using GuessMelody.Core.Models;
using GuessMelody.Core.Storage;
using System;
using System.Collections.Generic;
using System.IO;

namespace GuessMelody.Core.Game
{
    public static class PresetService
    {
        /// <summary>
        /// Сохранить все текущие настройки в один файл-пресет.
        /// </summary>
        public static void Save(string path,
                                GameSettings settings,
                                ButtonBindings buttons,
                                FolderConfig folders,
                                string presetName = null)
        {
            var preset = new GamePreset
            {
                SchemaVersion = 1,
                Name = presetName ?? Path.GetFileNameWithoutExtension(path),
                SavedAtUtc = DateTime.UtcNow,
                Game = settings,
                Buttons = buttons,
                Folders = folders
            };
            JsonStore.Save(path, preset);
        }

        /// <summary>
        /// Загрузить пресет из файла. Возвращает null, если файл битый.
        /// </summary>
        public static GamePreset Load(string path)
        {
            if (!File.Exists(path)) return null;
            try { return JsonStore.Load<GamePreset>(path); }
            catch { return null; }
        }

        /// <summary>
        /// Применить содержимое пресета к живым объектам, не пересоздавая их.
        /// Так UI не теряет подписки и не «мигает».
        /// </summary>
        public static void ApplyTo(GamePreset preset,
                                   GameSettings settings,
                                   ButtonBindings buttons,
                                   FolderConfig folders)
        {
            if (preset == null) return;

            // --- GameSettings ---
            if (preset.Game != null)
            {
                settings.PlayDurationSec = preset.Game.PlayDurationSec;
                settings.StartAtMode = preset.Game.StartAtMode;
                settings.StartAtSec = preset.Game.StartAtSec;
                settings.PlayRoundStartSound = preset.Game.PlayRoundStartSound;
                settings.RoundStartSoundFile = preset.Game.RoundStartSoundFile;
                settings.PlayCountdown = preset.Game.PlayCountdown;
                settings.CountdownSeconds = preset.Game.CountdownSeconds;
                settings.CountdownTickSoundFile = preset.Game.CountdownTickSoundFile;
                settings.CountdownEndSoundFile = preset.Game.CountdownEndSoundFile;
                settings.AnswerSeconds = preset.Game.AnswerSeconds;
                settings.AnswerTickSoundFile = preset.Game.AnswerTickSoundFile;
                settings.RightSoundFile = preset.Game.RightSoundFile;
                settings.WrongSoundFile = preset.Game.WrongSoundFile;
                settings.AutoNextRound = preset.Game.AutoNextRound;
                settings.MaxRounds = preset.Game.MaxRounds;
                settings.GridColumns = preset.Game.GridColumns;
                settings.GridRows = preset.Game.GridRows;
            }

            // --- ButtonBindings ---
            if (preset.Buttons != null)
            {
                buttons.Bindings.Clear();
                foreach (var b in preset.Buttons.Bindings)
                    buttons.Bindings.Add(new ButtonBinding
                    {
                        Mac = b.Mac,
                        DisplayName = b.DisplayName,
                        PlayerSlot = b.PlayerSlot
                    });
            }

            // --- FolderConfig ---
            if (preset.Folders != null)
            {
                folders.RootPath = preset.Folders.RootPath;
                folders.DefaultPreviewStartSec = preset.Folders.DefaultPreviewStartSec;

                // Копируем категории, а не ссылку на список
                folders.Categories.Clear();
                foreach (var c in preset.Folders.Categories)
                {
                    folders.Categories.Add(new CategoryConfig
                    {
                        Name = c.Name,
                        RelativePath = c.RelativePath,
                        Tracks = new List<string>(c.Tracks ?? new List<string>()),
                        FullPath = c.FullPath
                    });
                }

                // Копируем preview-настройки
                folders.PreviewStartSec.Clear();
                foreach (var kv in preset.Folders.PreviewStartSec ?? new Dictionary<string, double>())
                    folders.PreviewStartSec[kv.Key] = kv.Value;
            }
        }
    }
}