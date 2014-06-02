using System;
using System.IO;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.ServiceBus.Messaging;
using Newtonsoft.Json;

namespace Microsoft.Azure.Jobs.ServiceBus.Triggers
{
    internal class BrokeredMessageToUserTypeConverter : ITypeToObjectConverter<BrokeredMessage>
    {
        private Type _parameterType;

        public bool CanConvert(Type outputType)
        {
            // A slight abuse of the ITypeToObjectConverter contract, but it works for now.
            _parameterType = outputType;

            return true; // For now, put this converter last in the list and let it fail at runtime.
        }

        public object Convert(BrokeredMessage input)
        {
            string contents;

            using (Stream stream = input.GetBody<Stream>())
            using (TextReader reader = new StreamReader(stream))
            {
                contents = reader.ReadToEnd();
            }

            try
            {
                return JsonCustom.DeserializeObject(contents, _parameterType);
            }
            catch (JsonException e)
            {
                // Easy to have the queue payload not deserialize properly. So give a useful error. 
                string msg = string.Format(
@"Binding parameters to complex objects (such as '{0}') uses Json.NET serialization. 
1. Bind the parameter type as 'string' instead of '{0}' to get the raw values and avoid JSON deserialization, or
2. Change the queue payload to be valid json. The JSON parser failed: {1}
", _parameterType.Name, e.Message);
                throw new InvalidOperationException(msg);
            }
        }
    }
}
