using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GuessMelody.Services
{
    public class RecentFilesService
    {
        private const int MaxItems = 10;
        private readonly string _storePath;
        private readonly List<string> _files = new List<string>();

        public IReadOnlyList<string> Files => _files;

        public RecentFilesService()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GuessMelody");
            Directory.CreateDirectory(dir);
            _storePath = Path.Combine(dir, "recent.txt");
            Load();
        }

        public void Add(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            _files.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            _files.Insert(0, path);
            while (_files.Count > MaxItems) _files.RemoveAt(_files.Count - 1);
            Save();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_storePath)) return;
                _files.Clear();
                foreach (var line in File.ReadAllLines(_storePath))
                    if (!string.IsNullOrWhiteSpace(line)) _files.Add(line);
            }
            catch { }
        }

        private void Save()
        {
            try { File.WriteAllLines(_storePath, _files.ToArray()); }
            catch { }
        }
    }
}