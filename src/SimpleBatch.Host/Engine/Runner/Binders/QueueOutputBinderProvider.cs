using System;
using System.Reflection;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;



namespace Microsoft.WindowsAzure.Jobs
{
    internal class QueueOutputBinderProvider : ICloudBinderProvider
    {
        public ICloudBinder TryGetBinder(Type targetType)
        {
            if (targetType.IsGenericType)
            {
                if (targetType.GetGenericTypeDefinition() == typeof(IQueueOutput<>))
                {
                    var args = targetType.GetGenericArguments();
                    var t2 = typeof(QueueBinder<>).MakeGenericType(args[0]);

                    return new QueueOutputBinder { queueType = t2 };
                }
            }

            if (targetType == typeof(CloudQueue))
            {
                return new CloudQueueOutputBinder();
            }

            return null;
        }

        private static CloudQueue GetCloudQueue(IBinderEx bindingContext, ParameterInfo parameter)
        {
            CloudStorageAccount account = Utility.GetAccount(bindingContext.AccountConnectionString);

            // How to get q-name? Or other info from the attributes.
            string queueName = parameter.Name;

            var q = account.CreateCloudQueueClient().GetQueueReference(queueName);
            q.CreateIfNotExist();
            return q;
        }

        private class CloudQueueOutputBinder : ICloudBinder
        {
            public BindResult Bind(IBinderEx bindingContext, ParameterInfo parameter)
            {
                return new BindResult { Result = GetCloudQueue(bindingContext, parameter) };
            }
        }

        private class QueueOutputBinder : ICloudBinder
        {
            public Type queueType;

            public BindResult Bind(IBinderEx bindingContext, ParameterInfo parameter)
            {
                var q = GetCloudQueue(bindingContext, parameter);
                var obj = Activator.CreateInstance(queueType, new object[] { q });
                return new BindResult { Result = obj };
            }
        }
    }
}