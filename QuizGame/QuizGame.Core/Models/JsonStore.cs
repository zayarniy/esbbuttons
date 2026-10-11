using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace QuizGame.QuizGame.Core
{
    public static class JsonStore
    {
        private static readonly JsonSerializerSettings Js = new JsonSerializerSettings
        {
            Formatting = Newtonsoft.Json.Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Include
        };

        public static void Save<T>(string path, T obj) =>
            File.WriteAllText(path, JsonConvert.SerializeObject(obj, Js), Encoding.UTF8);

        public static T Load<T>(string path) where T : new()
        {
            if (!File.Exists(path))
                return new T();

            string json = File.ReadAllText(path, Encoding.UTF8);
            T result = JsonConvert.DeserializeObject<T>(json, Js);
            return result != null ? result : new T();
        }
    }
    
}
