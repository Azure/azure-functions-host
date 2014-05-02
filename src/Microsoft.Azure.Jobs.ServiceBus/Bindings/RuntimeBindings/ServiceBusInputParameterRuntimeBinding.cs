using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.ServiceBus.Messaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.Jobs
{
    internal class ServiceBusInputParameterRuntimeBinding : ParameterRuntimeBinding
    {
        private static readonly UTF8Encoding strictUTF8Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);
        public string Content { get; set; }

        private BrokeredMessage _message;
        
        [JsonIgnore]
        public BrokeredMessage Message
        {
            get { return _message; }
            set
            {
                _message = value;
                try
                {
                    var bytes = new MemoryStream();
                    value.Clone().GetBody<Stream>().CopyTo(bytes);
                    Content = strictUTF8Encoding.GetString(bytes.ToArray());
                }
                catch (DecoderFallbackException)
                {
                    Content = "byte[" + Message.Size + "]";
                }
            }
        }

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

            if (parameterType == typeof(BrokeredMessage))
            {
                result = Message;
            }
            else if (parameterType == typeof(object))
            {
                // If the user asks for object, give them the raw string form.
                // We don't have a more derived type to serialize as. 
                result = AsString(Message);
            }
            else if (parameterType == typeof(string))
            {
                result = AsString(Message);
            }
            else if (parameterType == typeof(byte[]))
            {
                result = AsBytes(Message);
            }
            else
            {
                try
                {
                    // Use contentType field for JSON detection - use default .GetBody if it is not application/json
                    result = JsonCustom.DeserializeObject(AsString(Message), parameterType);
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

        private static byte[] AsBytes(BrokeredMessage msg)
        {
            using (var data = new MemoryStream())
            {
                msg.GetBody<Stream>().CopyTo(data);
                return data.ToArray();
            }
        }

        private static string AsString(BrokeredMessage msg)
        {
            return new StreamReader(msg.GetBody<Stream>()).ReadToEnd();
        }
    }
}