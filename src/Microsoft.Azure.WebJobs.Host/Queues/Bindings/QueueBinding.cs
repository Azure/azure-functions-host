// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class QueueBinding : IBinding
    {
        private readonly string _parameterName;
        private readonly IArgumentBinding<CloudQueue> _argumentBinding;
        private readonly CloudQueueClient _client;
        private readonly string _accountName;
        private readonly IBindableQueuePath _path;
        private readonly IObjectToTypeConverter<CloudQueue> _converter;

        public QueueBinding(string parameterName, IArgumentBinding<CloudQueue> argumentBinding, CloudQueueClient client,
            IBindableQueuePath path)
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

        private static IObjectToTypeConverter<CloudQueue> CreateConverter(CloudQueueClient client, IBindableQueuePath path)
        {
            return new CompositeObjectToTypeConverter<CloudQueue>(
                new OutputConverter<CloudQueue>(new IdentityConverter<CloudQueue>()),
                new OutputConverter<string>(new StringToCloudQueueConverter(client, path)));
        }

        public Task<IValueProvider> BindAsync(BindingContext context)
        {
            string boundQueueName = _path.Bind(context.BindingData);
            CloudQueue queue = _client.GetQueueReference(boundQueueName);

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
                QueueName = _path.QueueNamePattern,
                Access = Access
            };
        }
    }
}
