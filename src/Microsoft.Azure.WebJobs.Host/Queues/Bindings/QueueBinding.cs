// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class QueueBinding : IBinding
    {
        private readonly string _parameterName;
        private readonly IArgumentBinding<IStorageQueue> _argumentBinding;
        private readonly IStorageQueueClient _client;
        private readonly string _accountName;
        private readonly IBindableQueuePath _path;
        private readonly IObjectToTypeConverter<IStorageQueue> _converter;

        public QueueBinding(string parameterName, IArgumentBinding<IStorageQueue> argumentBinding,
            IStorageQueueClient client, IBindableQueuePath path)
        {
            _parameterName = parameterName;
            _argumentBinding = argumentBinding;
            _client = client;
            _accountName = QueueClient.GetAccountName(client);
            _path = path;
            _converter = CreateConverter(client, path);
        }

        public bool FromAttribute
        {
            get { return true; }
        }

        public string QueueName
        {
            get { return _path.QueueNamePattern; }
        }

        private FileAccess Access
        {
            get
            {
                return _argumentBinding.ValueType == typeof(CloudQueue)
                    ? FileAccess.ReadWrite : FileAccess.Write;
            }
        }

        private static IObjectToTypeConverter<IStorageQueue> CreateConverter(IStorageQueueClient client,
            IBindableQueuePath path)
        {
            return new CompositeObjectToTypeConverter<IStorageQueue>(
                new OutputConverter<IStorageQueue>(new IdentityConverter<IStorageQueue>()),
                new OutputConverter<CloudQueue>(new CloudQueueToStorageQueueConverter()),
                new OutputConverter<string>(new StringToStorageQueueConverter(client, path)));
        }

        private Task<IValueProvider> BindQueueAsync(IStorageQueue value, ValueBindingContext context)
        {
            return _argumentBinding.BindAsync(value, context);
        }

        public Task<IValueProvider> BindAsync(BindingContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            string boundQueueName = _path.Bind(context.BindingData);
            IStorageQueue queue = _client.GetQueueReference(boundQueueName);

            return BindQueueAsync(queue, context.ValueContext);
        }

        public Task<IValueProvider> BindAsync(object value, ValueBindingContext context)
        {
            IStorageQueue queue = null;

            if (!_converter.TryConvert(value, out queue))
            {
                throw new InvalidOperationException("Unable to convert value to CloudQueue.");
            }

            return BindQueueAsync(queue, context);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new QueueParameterDescriptor
            {
                Name = _parameterName,
                AccountName = _accountName,
                QueueName = _path.QueueNamePattern,
                Access = Access
            };
        }
    }
}
