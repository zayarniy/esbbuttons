using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuessMelody.Core.Models;
using GuessMelody.Core.Serialization;

namespace GuessMelody.Services
{
    public class SettingsPresetService
    {
        private readonly string _dir;

        public SettingsPresetService()
        {
            _dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GuessMelody", "presets");
            Directory.CreateDirectory(_dir);
        }

        public IReadOnlyList<string> ListNames()
        {
            try
            {
                return Directory.GetFiles(_dir, "*.json")
                    .Select(Path.GetFileNameWithoutExtension)
                    .OrderBy(n => n)
                    .ToList();
            }
            catch { return new List<string>(); }
        }

        public SettingsPreset Load(string name)
        {
            var path = Path.Combine(_dir, name + ".json");
            return JsonStore.Load<SettingsPreset>(path);
        }

        public bool Save(string name, SettingsPreset preset)
        {
            var path = Path.Combine(_dir, name + ".json");
            preset.Name = name;
            return JsonStore.Save(path, preset);
        }

        public bool Delete(string name)
        {
            try
            {
                var path = Path.Combine(_dir, name + ".json");
                if (File.Exists(path)) File.Delete(path);
                return true;
            }
            catch { return false; }
        }
    }
}