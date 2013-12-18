using System;
using System.Reflection;
using Microsoft.WindowsAzure.StorageClient;

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

            if (parameterType.IsAssignableFrom(typeof(string)))
            {
                result = Content;
            }
            else if (parameterType.IsAssignableFrom(typeof(byte[])))
            {
                result = AsBytes(Content);
            }
            else
            {
                result = JsonCustom.DeserializeObject(Content, parameterType);
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