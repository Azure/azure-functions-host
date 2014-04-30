using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.Jobs
{
    // The queue input static binding just flows the raw queue payload through to the runtime binding. 
    // The runtime binding provides the actual structure and interpretation (because it's the runtime
    // binding that has the target parameter type).
    internal class QueueInputParameterRuntimeBinding : ParameterRuntimeBinding
    {
        public string Content { get; set; }

        public override string ConvertToInvokeString()
        {
            return Content;
        }

        // Given a parameter type, get the route parameters it might populate.
        // This only applies to parameter types that deserialize to structured data. 
        public static string[] GetRouteParametersFromParamType(Type type)
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
        public static IDictionary<string, string> GetRouteParameters(string json, string[] namedParams)
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
            catch(JsonException)
            {
                return null;
            }
        }

        public override BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter)
        {
            if (targetParameter == null)
            {
                throw new ArgumentNullException("targetParameter");
            }

            Type parameterType = targetParameter.ParameterType;
            object result;

            if (parameterType == typeof(object))
            {
                // If the user asks for object, give them the raw string form.
                // We don't have a more derived type to serialize as. 
                result = Content;
            }
            else if (parameterType == typeof(string))
            {                
                result = Content;
            }
            else if (parameterType == typeof(byte[]))
            {
                // Use IsAssignableFrom to include interfaces implemeneted by byte[], like IEnumerable<Byte>
                result = AsBytes(Content);
            }
            else
            {
                try
                {
                    result = JsonCustom.DeserializeObject(Content, parameterType);
                }
                catch (JsonException e)
                {
                    // Easy to have the queue payload not deserialize properly. So give a useful error. 
                    string msg = string.Format(
@"Binding parameters to complex objects (such as '{0}') uses Json.NET serialization. 
1. Bind the parameter type as 'string' instead of '{0}' to get the raw values and avoid JSON deserialization, or
2. Change the queue payload to be valid json. The JSON parser failed: {1}
", parameterType.Name, e.Message); 
                    throw new InvalidOperationException(msg);
                }
            }

            return new BindResult { Result = result };
        }

        private static byte[] AsBytes(string content)
        {
            // Let CloudQueueMessage own the contract for queue message as byte[].
            CloudQueueMessage message = new CloudQueueMessage(content);
            return message.AsBytes;
        }
    }
}
