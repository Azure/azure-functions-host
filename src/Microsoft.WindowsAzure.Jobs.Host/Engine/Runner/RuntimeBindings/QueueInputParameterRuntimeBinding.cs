using System;
using System.Reflection;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class QueueInputParameterRuntimeBinding : ParameterRuntimeBinding
    {
        public string Content { get; set; }

        public override string ConvertToInvokeString()
        {
            return Content;
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
@"Binding parameters to complex objects (such as '{0}') uses JSON.Net serialization. 
1. Bind the parameter type as 'string' instead of '{0}' to get the raw values and avoid JSON deserialization, or
2. Change the queue payload to be valid json. The json parser failed: {1}
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
