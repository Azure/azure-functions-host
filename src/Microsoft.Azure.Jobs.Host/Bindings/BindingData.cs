using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal static class BindingData
    {
        public static IReadOnlyDictionary<string, Type> GetContract(Type type)
        {
            if ((type == typeof(object)) ||
                (type == typeof(string)) ||
                (type == typeof(byte[])))
            {
                // No binding data is available for primitive types.
                return null;
            }

            // The properties on user-defined types are valid binding data.
            IReadOnlyList<PropertyInfo> bindingDataProperties =
                type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => ObjectBinderHelpers.UseToStringParser(p.PropertyType))
                    .ToList();

            if (bindingDataProperties.Count == 0)
            {
                return null;
            }

            Dictionary<string, Type> contract = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

            foreach (PropertyInfo property in bindingDataProperties)
            {
                contract.Add(property.Name, property.PropertyType);
            }

            return contract;
        }

        public static IReadOnlyDictionary<string, object> GetBindingData(string json, IReadOnlyDictionary<string, Type> contract)
        {
            if ((contract == null) || (json == null))
            {
                return null;
            }

            JObject obj;

            try
            {
                obj = JObject.Parse(json);
            }
            catch
            {
                return null;
            }

            Dictionary<string, object> d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var param in contract)
            {
                JToken token;
                if (obj.TryGetValue(param.Key, out token))
                {
                    // Only include simple types (ints, strings, etc).
                    JValue value = token as JValue;
                    if (value != null)
                    {
                        object unconvertedValue = value.Value;
                        Type expectedType = param.Value;
                        object convertedValue;

                        try
                        {
                            convertedValue = Convert.ChangeType(unconvertedValue, expectedType);
                        }
                        catch
                        {
                            // Skip values that can't be converted to the correct type.
                            continue;
                        }

                        d.Add(param.Key, convertedValue);
                    }
                }
            }

            return d;
        }
    }
}
