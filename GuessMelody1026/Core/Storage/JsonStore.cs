using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.IO;
using System.Text;
using System.Xml;

namespace GuessMelody.Core.Storage
{
    /// <summary>
    /// Простое JSON-хранилище с дефолтными значениями при отсутствии файла.
    /// </summary>
    public static class JsonStore
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Newtonsoft.Json.Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            Converters = { new StringEnumConverter() },
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        public static T Load<T>(string path) where T : new()
        {
            if (!File.Exists(path)) return new T();

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return new T();
                //return JsonConvert.DeserializeObject<T>(json, Settings) ?? new T();
                var result = JsonConvert.DeserializeObject<T>(json, Settings);
                return result == null ? new T() : result;
            }
            catch (Exception ex)
            {
                // Не роняем приложение, если пользователь покорёжил JSON.
                // Файл не перезаписываем — оставляем пользователю шанс восстановить.
                Console.Error.WriteLine($"[JsonStore] Ошибка чтения {path}: {ex.Message}");
                return new T();
            }
        }

        public static void Save<T>(string path, T value)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonConvert.SerializeObject(value, Settings);

            // Атомарная запись: пишем во временный и переименовываем.
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));

            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }
    }
}