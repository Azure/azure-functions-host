using Microsoft.Azure.Jobs.Host.Converters;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal class StringToServiceBusEntityConverter : IConverter<string, ServiceBusEntity>
    {
        private readonly ServiceBusAccount _account;

        public StringToServiceBusEntityConverter(ServiceBusAccount account)
        {
            _account = account;
        }

        public ServiceBusEntity Convert(string input)
        {
            return new ServiceBusEntity
            {
                Account = _account,
                MessageSender = _account.MessagingFactory.CreateMessageSender(input)
            };
        }
    }
}
