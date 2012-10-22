using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace RunnerInterfaces
{
    public static class JsonCustom
    {
        public static JsonSerializerSettings _settings = new JsonSerializerSettings()
        {
            TypeNameHandling = TypeNameHandling.Auto            
        };

        public static JsonSerializerSettings SerializerSettings
        {
            get
            {
                return _settings;
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
