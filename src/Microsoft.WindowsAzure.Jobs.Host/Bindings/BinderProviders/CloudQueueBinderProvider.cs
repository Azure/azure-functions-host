using System;
using System.Reflection;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.WindowsAzure.Jobs.Host.Bindings.BinderProviders
{
    internal class CloudQueueBinderProvider : ICloudBinderProvider
    {
        public ICloudBinder TryGetBinder(Type targetType)
        {
            if (targetType == typeof(CloudQueue))
            {
                return new CloudQueueBinder();
            }
            return null;
        }

        private class CloudQueueBinder : ICloudBinder, ICloudBinderVerify
        {
            public BindResult Bind(IBinderEx bindingContext, ParameterInfo parameter)
            {
                CloudStorageAccount account = Utility.GetAccount(bindingContext.AccountConnectionString);
                string queueName = parameter.Name;

                var queue = account.CreateCloudQueueClient().GetQueueReference(queueName);
                queue.CreateIfNotExists();

                return new BindResult { Result = queue };
            }

            void ICloudBinderVerify.Validate(ParameterInfo parameter)
            {
                string queueName = parameter.Name;
                QueueClient.ValidateQueueName(queueName);
            }
        }
    }
}
