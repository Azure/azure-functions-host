using System;
using Microsoft.Azure.Jobs.Host.Converters;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal class StringToServiceBusEntityConverter : IConverter<string, ServiceBusEntity>
    {
        private readonly ServiceBusEntity _defaultEntity;

        public StringToServiceBusEntityConverter(ServiceBusEntity defaultEntity)
        {
            _defaultEntity = defaultEntity;
        }

        public ServiceBusEntity Convert(string input)
        {
            // For convenience, treat an an empty string as a request for the default value.
            if (String.IsNullOrEmpty(input))
            {
                return _defaultEntity;
            }

            return new ServiceBusEntity
            {
                Account = _defaultEntity.Account,
                MessageSender = _defaultEntity.Account.MessagingFactory.CreateMessageSender(input)
            };
        }
    }
}
