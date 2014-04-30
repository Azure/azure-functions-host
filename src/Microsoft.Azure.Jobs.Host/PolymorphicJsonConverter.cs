using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Microsoft.Azure.Jobs.Host
{
    /// <remarks>
    /// Unlike $type in JSON.NET, this converter decouples the message data from the .NET class and assembly names.
    /// It also allows emitting a type on the root object.
    /// </remarks>
    internal class PolymorphicJsonConverter : JsonConverter
    {
        private readonly string _typePropertyName;
        private readonly IDictionary<string, Type> _nameToTypeMap;
        private readonly IDictionary<Type, string> _typeToNameMap;

        public PolymorphicJsonConverter(IDictionary<string, Type> typeMapping)
            : this("$$type", typeMapping)
        {
        }

        public PolymorphicJsonConverter(string typePropertyName, IDictionary<string, Type> typeMapping)
        {
            if (typePropertyName == null)
            {
                throw new ArgumentNullException("typePropertyName");
            }

            if (typeMapping == null)
            {
                throw new ArgumentNullException("typeMapping");
            }

            _typePropertyName = typePropertyName;
            _nameToTypeMap = typeMapping;

            _typeToNameMap = new Dictionary<Type, string>();

            foreach (KeyValuePair<string, Type> item in _nameToTypeMap)
            {
                _typeToNameMap.Add(item.Value, item.Key);
            }
        }

        public string TypePropertyName
        {
            get { return _typePropertyName; }
        }

        public override bool CanConvert(Type objectType)
        {
            return _typeToNameMap.ContainsKey(objectType);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader == null)
            {
                throw new ArgumentNullException("reader");
            }

            if (objectType == null)
            {
                throw new NotSupportedException(
                    "PolymorphicJsonConverter does not support deserializing without specifying a default objectType.");
            }

            if (serializer == null)
            {
                throw new ArgumentNullException("serializer");
            }

            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            JToken json = JToken.ReadFrom(reader);

            Type typeToCreate = GetTypeToCreate(json) ?? objectType;

            object target = Activator.CreateInstance(typeToCreate);
            serializer.Populate(json.CreateReader(), target);
            return target;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (writer == null)
            {
                throw new ArgumentNullException("writer");
            }

            if (serializer == null)
            {
                throw new ArgumentNullException("serializer");
            }

            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            Type valueType = value.GetType();

            serializer.ContractResolver = new NonCircularContractResolver(valueType);

            JObject json = JObject.FromObject(value, serializer);

            string typeName = GetTypeName(valueType);

            if (typeName != null)
            {
                if (json.Property(_typePropertyName) != null)
                {
                    json.Remove(_typePropertyName);
                }

                json.AddFirst(new JProperty(_typePropertyName, typeName));
            }

            serializer.Serialize(writer, json);
        }

        public static IDictionary<string, Type> GetTypeMapping<T>()
        {
            IDictionary<string, Type> typeMapping = new Dictionary<string, Type>();

            foreach (Type type in GetTypesInHierarchy<T>())
            {
                typeMapping.Add(GetDeclaredTypeName(type), type);
            }

            return typeMapping;
        }

        private static IEnumerable<Type> GetTypesInHierarchy<T>()
        {
            return typeof(T).Assembly.GetTypes().Where(t => (typeof(T).IsAssignableFrom(t)));
        }

        private static string GetDeclaredTypeName(Type type)
        {
            Debug.Assert(type != null);

            JsonTypeNameAttribute[] attributes = (JsonTypeNameAttribute[])type.GetCustomAttributes(
                typeof(JsonTypeNameAttribute), inherit: false);

            if (attributes != null && attributes.Length > 0)
            {
                return attributes[0].TypeName;
            }

            return type.Name;
        }

        private string GetTypeName(Type type)
        {
            if (!_typeToNameMap.ContainsKey(type))
            {
                return null;
            }

            return _typeToNameMap[type];
        }

        private Type GetTypeToCreate(JToken token)
        {
            JObject tokenObject = token as JObject;

            if (tokenObject == null)
            {
                return null;
            }

            JProperty typeProperty = tokenObject.Property(_typePropertyName);

            if (typeProperty == null)
            {
                return null;
            }

            JValue typeValue = typeProperty.Value as JValue;

            if (typeValue == null)
            {
                return null;
            }

            string typeString = typeValue.Value as string;

            if (typeString == null)
            {
                return null;
            }

            if (!_nameToTypeMap.ContainsKey(typeString))
            {
                return null;
            }

            return _nameToTypeMap[typeString];
        }

        private class NonCircularContractResolver : DefaultContractResolver
        {
            private readonly Type _contractType;

            public NonCircularContractResolver(Type contractType)
            {
                Debug.Assert(contractType != null);
                _contractType = contractType;
            }

            protected override JsonContract CreateContract(Type objectType)
            {
                JsonContract contract = base.CreateContract(objectType);

                if (_contractType.IsAssignableFrom(objectType))
                {
                    contract.Converter = null;
                }

                return contract;
            }
        }
    }
}
