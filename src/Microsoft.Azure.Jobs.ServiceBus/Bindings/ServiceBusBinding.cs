using System;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal class ServiceBusBinding : IBinding
    {
        private readonly IArgumentBinding<ServiceBusEntity> _argumentBinding;
        private readonly ServiceBusAccount _account;
        private readonly string _namespaceName;
        private readonly string _queueOrTopicName;
        private readonly IObjectToTypeConverter<ServiceBusEntity> _converter;

        public ServiceBusBinding(IArgumentBinding<ServiceBusEntity> argumentBinding, ServiceBusAccount account,
            string queueOrTopicName)
        {
            _argumentBinding = argumentBinding;
            _account = account;
            _namespaceName = ServiceBusClient.GetNamespaceName(account);
            _queueOrTopicName = queueOrTopicName;
            _converter = CreateConverter(account, queueOrTopicName);
        }

        public bool FromAttribute
        {
            get { return true; }
        }

        private static IObjectToTypeConverter<ServiceBusEntity> CreateConverter(ServiceBusAccount account,
            string queueOrTopicName)
        {
            return new OutputConverter<string>(new StringToServiceBusEntityConverter(account, queueOrTopicName));
        }

        public IValueProvider Bind(BindingContext context)
        {
            ServiceBusEntity entity = new ServiceBusEntity
            {
                Account = _account,
                MessageSender = _account.MessagingFactory.CreateMessageSender(_queueOrTopicName),
            };
            return Bind(entity, context);
        }

        private IValueProvider Bind(ServiceBusEntity value, ArgumentBindingContext context)
        {
            return _argumentBinding.Bind(value, context);
        }

        public IValueProvider Bind(object value, ArgumentBindingContext context)
        {
            ServiceBusEntity entity = null;

            if (!_converter.TryConvert(value, out entity))
            {
                throw new InvalidOperationException("Unable to convert value to ServiceBusEntity.");
            }

            return Bind(entity, context);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new ServiceBusParameterDescriptor
            {
                NamespaceName = _namespaceName,
                QueueOrTopicName = _queueOrTopicName
            };
        }
    }
}
