using System;
using Newtonsoft.Json;

namespace Microsoft.Azure.Jobs.Host
{
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
}
