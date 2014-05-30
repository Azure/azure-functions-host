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
            return new ServiceBusParameterStaticBinding
            {
                EntityPath = attr.QueueOrTopicName
            };
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