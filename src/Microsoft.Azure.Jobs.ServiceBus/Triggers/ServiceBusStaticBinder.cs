using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.ServiceBus.Messaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.Jobs
{
    internal class ServiceBusStaticBinder
    {
        static ParameterStaticBinding Bind(ServiceBusAttribute attr, ParameterInfo parameter)
        {
            var entityPath = parameter.Name;
            if (!String.IsNullOrEmpty(attr.EntityName))
            {
                entityPath = attr.EntityName;
            }
            else if (!String.IsNullOrEmpty(attr.Subscription))
            {
                entityPath = SubscriptionClient.FormatSubscriptionPath(attr.Topic, attr.Subscription);
            }

            string[] namedParams = GetRouteParametersFromParamType(parameter.ParameterType);

            return new ServiceBusParameterStaticBinding
            {
                EntityPath = entityPath,
                IsInput = !parameter.IsOut,
                Params = namedParams
            };
        }

        // Given a parameter type, get the route parameters it might populate.
        // This only applies to parameter types that deserialize to structured data. 
        internal static string[] GetRouteParametersFromParamType(Type type)
        {
            if ((type == typeof(object)) ||
                (type == typeof(string)) ||
                (type == typeof(byte[])))
            {
                // Not a structure result, so no route parameters.
                return null;
            }

            // It's a structured result, so provide route parameters for any simple property types.
            var props = from prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        where ObjectBinderHelpers.UseToStringParser(prop.PropertyType)
                        select prop.Name;

            var array = props.ToArray();
            if (array.Length == 0)
            {
                return null;
            }
            return array;
        }

        // Get the values for a set of route parameters. 
        // This assumes the parameter is structured (and deserialized with Json). If that wasn't true, 
        // then namedParams should have been empty. 
        internal static IDictionary<string, string> GetRouteParameters(string json, string[] namedParams)
        {
            if ((namedParams == null) || (json == null))
            {
                return null;
            }

            try
            {
                IDictionary<string, string> d = new Dictionary<string, string>();
                JObject obj = JObject.Parse(json);

                foreach (var param in namedParams)
                {
                    JToken token;
                    if (obj.TryGetValue(param, out token))
                    {
                        // Only include simple types (ints, strings, etc).
                        JToken value = token as JValue;
                        if (value != null)
                        {
                            d.Add(param, value.ToString());
                        }
                    }
                }
                return d;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}