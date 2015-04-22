// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Bindings
{
    internal class ServiceBusBinding : IBinding
    {
        private readonly string _parameterName;
        private readonly IArgumentBinding<ServiceBusEntity> _argumentBinding;
        private readonly ServiceBusAccount _account;
        private readonly string _namespaceName;
        private readonly IBindableServiceBusPath _path;
        private readonly IAsyncObjectToTypeConverter<ServiceBusEntity> _converter;

        public ServiceBusBinding(string parameterName, IArgumentBinding<ServiceBusEntity> argumentBinding,
            ServiceBusAccount account, IBindableServiceBusPath path)
        {
            _parameterName = parameterName;
            _argumentBinding = argumentBinding;
            _account = account;
            _namespaceName = ServiceBusClient.GetNamespaceName(account);
            _path = path;
            _converter = CreateConverter(account, path);
        }

        public bool FromAttribute
        {
            get { return true; }
        }

        private static IAsyncObjectToTypeConverter<ServiceBusEntity> CreateConverter(ServiceBusAccount account,
            IBindableServiceBusPath queueOrTopicName)
        {
            return new OutputConverter<string>(new StringToServiceBusEntityConverter(account, queueOrTopicName));
        }

        public async Task<IValueProvider> BindAsync(BindingContext context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            string boundQueueName = _path.Bind(context.BindingData);
            MessageSender messageSender = await _account.MessagingFactory.CreateMessageSenderAsync(boundQueueName);

            ServiceBusEntity entity = new ServiceBusEntity
            {
                Account = _account,
                MessageSender = messageSender
            };
            return await BindAsync(entity, context.ValueContext);
        }

        private Task<IValueProvider> BindAsync(ServiceBusEntity value, ValueBindingContext context)
        {
            return _argumentBinding.BindAsync(value, context);
        }

        public async Task<IValueProvider> BindAsync(object value, ValueBindingContext context)
        {
            ConversionResult<ServiceBusEntity> conversionResult = await _converter.TryConvertAsync(value,
                context.CancellationToken);

            if (!conversionResult.Succeeded)
            {
                throw new InvalidOperationException("Unable to convert value to ServiceBusEntity.");
            }

            return await BindAsync(conversionResult.Result, context);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new ServiceBusParameterDescriptor
            {
                Name = _parameterName,
                NamespaceName = _namespaceName,
                QueueOrTopicName = _path.QueueOrTopicNamePattern
            };
        }
    }
}
