using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure;
using Newtonsoft.Json;

namespace RunnerInterfaces
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
            settings.Converters.Add(new StorageConverter());

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

    // See example: http://stackoverflow.com/questions/7585593/how-do-i-configure-json-net-custom-serialization
    // General purpose converted for types that have string conversion. 
    internal abstract class StringConverter<T> : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return (objectType == typeof(T));
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (objectType != typeof(T))
            {
                throw new InvalidOperationException("Illegal objectType");
            }
            var t1 = reader.TokenType;
            if (t1 == JsonToken.Null || t1 == JsonToken.None)
            {
                return null; // missing. common case.
            }
            if (t1 == JsonToken.String)
            {
                string value = (string)reader.Value;
                return this.ReadFromString(value); ;
            }
            throw new InvalidOperationException("Illegal JSON. Expecting string token for item");
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            T path = (T)value;
            string x = this.GetAsString(path);
            writer.WriteValue(x);
        }

        public virtual string GetAsString(T value)
        {
            return value.ToString();
        }
        public abstract object ReadFromString(string value);
    }

    class StorageConverter : StringConverter<CloudStorageAccount>
    {
        public override string GetAsString(CloudStorageAccount value)
        {
            return value.ToString(exportSecrets: true);
        }
        public override object ReadFromString(string value)
        {
            return CloudStorageAccount.Parse(value);
        }
    }
}
