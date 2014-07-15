using System;
using Newtonsoft.Json;

namespace Microsoft.Azure.Jobs.Host
{
    internal static class JsonCustom
    {
        public static JsonSerializerSettings NewSettings()
        {
            var settings = new JsonSerializerSettings()
            {
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                Formatting = Formatting.Indented
            };

            return settings;
        }

        private static JsonSerializerSettings NewSettings2()
        {
            var settings = NewSettings();
            settings.TypeNameHandling = TypeNameHandling.All;
            return settings;
        }

        public static JsonSerializerSettings _settingsTypeNameAll = NewSettings2();
        public static JsonSerializerSettings _settings = NewSettings();

        public static JsonSerializerSettings SerializerSettings
        {
            get
            {
                return _settings;
            }
        }

        // When serializing polymorphic objects, JSON won't emit type tags for top-level objects. 
        // Ideally. Json.Net would have this hook, but the request was resolved won't-fix: 
        // See http://json.codeplex.com/workitem/22202 
        public static string SerializeObject(object o, Type type)
        {
            if (o.GetType() != type)
            {
                // For the $type tag to always be emitted.
                return JsonConvert.SerializeObject(o, _settingsTypeNameAll);
            }
            else
            {
                return SerializeObject(o);
            }
        }

        public static string SerializeObject(object o)
        {
            return JsonConvert.SerializeObject(o, _settings);
        }

        public static T DeserializeObject<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, _settings);
        }

        public static object DeserializeObject(string json, Type type)
        {
            return JsonConvert.DeserializeObject(json, type, _settings);
        }
    }
}
