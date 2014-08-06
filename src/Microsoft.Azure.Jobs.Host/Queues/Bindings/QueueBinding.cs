// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Bindings
{
    internal class QueueBinding : IBinding
    {
        private readonly string _parameterName;
        private readonly IArgumentBinding<CloudQueue> _argumentBinding;
        private readonly CloudQueueClient _client;
        private readonly string _accountName;
        private readonly string _queueName;
        private readonly IObjectToTypeConverter<CloudQueue> _converter;

        public QueueBinding(string parameterName, IArgumentBinding<CloudQueue> argumentBinding, CloudQueueClient client,
            string queueName)
        {
            _parameterName = parameterName;
            _argumentBinding = argumentBinding;
            _client = client;
            _accountName = QueueClient.GetAccountName(client);
            _queueName = queueName;
            _converter = CreateConverter(client, queueName);
        }

        public bool FromAttribute
        {
            get { return true; }
        }

        public string QueueName
        {
            get { return _queueName; }
        }

        private FileAccess Access
        {
            get
            {
                return _argumentBinding.ValueType == typeof(CloudQueue)
                    ? FileAccess.ReadWrite : FileAccess.Write;
            }
        }

        private static IObjectToTypeConverter<CloudQueue> CreateConverter(CloudQueueClient client, string queueName)
        {
            return new CompositeObjectToTypeConverter<CloudQueue>(
                new OutputConverter<CloudQueue>(new IdentityConverter<CloudQueue>()),
                new OutputConverter<string>(new StringToCloudQueueConverter(client, queueName)));
        }

        public Task<IValueProvider> BindAsync(BindingContext context)
        {
            CloudQueue queue = _client.GetQueueReference(_queueName);
            return BindAsync(queue, context.ValueContext);
        }

        private Task<IValueProvider> BindAsync(CloudQueue value, ValueBindingContext context)
        {
            return _argumentBinding.BindAsync(value, context);
        }

        public Task<IValueProvider> BindAsync(object value, ValueBindingContext context)
        {
            CloudQueue queue = null;

            if (!_converter.TryConvert(value, out queue))
            {
                throw new InvalidOperationException("Unable to convert value to CloudQueue.");
            }

            return BindAsync(queue, context);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new QueueParameterDescriptor
            {
                Name = _parameterName,
                AccountName = _accountName,
                QueueName = _queueName,
                Access = Access
            };
        }
    }
}
