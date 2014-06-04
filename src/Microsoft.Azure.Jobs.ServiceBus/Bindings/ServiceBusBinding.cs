using System;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal class ServiceBusBinding : IBinding
    {
        private readonly IArgumentBinding<ServiceBusEntity> _argumentBinding;
        private readonly ServiceBusEntity _entity;
        private readonly IObjectToTypeConverter<ServiceBusEntity> _converter;

        public ServiceBusBinding(IArgumentBinding<ServiceBusEntity> argumentBinding, ServiceBusEntity entity)
        {
            _argumentBinding = argumentBinding;
            _entity = entity;
            _converter = CreateConverter(entity);
        }

        private static IObjectToTypeConverter<ServiceBusEntity> CreateConverter(ServiceBusEntity entity)
        {
            return new OutputConverter<string>(new StringToServiceBusEntityConverter(entity));
        }

        public IValueProvider Bind(BindingContext context)
        {
            return Bind(_entity, context);
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
                QueueOrTopicName = _entity.MessageSender.Path
            };
        }
    }
}
