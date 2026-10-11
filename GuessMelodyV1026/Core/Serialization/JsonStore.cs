using System;
using System.IO;
using Newtonsoft.Json;

namespace GuessMelody.Core.Serialization
{
    public static class JsonStore
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };

        public static T Load<T>(string path) where T : class
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try
            {
                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<T>(json, Settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JsonStore.Load error: {ex.Message}");
                return null;
            }
        }

        public static bool Save<T>(string path, T obj)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonConvert.SerializeObject(obj, Settings);
                File.WriteAllText(path, json);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JsonStore.Save error: {ex.Message}");
                return false;
            }
        }
    }
}