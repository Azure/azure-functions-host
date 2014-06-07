using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Triggers
{
    internal class QueueTriggerBinding : ITriggerBinding<CloudQueueMessage>
    {
        private static readonly IObjectToTypeConverter<CloudQueueMessage> _converter = new CompositeObjectToTypeConverter<CloudQueueMessage>(
            new OutputConverter<CloudQueueMessage>(new IdentityConverter<CloudQueueMessage>()),
            new OutputConverter<string>(new StringToCloudQueueMessageConverter()));

        private readonly IArgumentBinding<CloudQueueMessage> _argumentBinding;
        private readonly string _accountName;
        private readonly string _queueName;

        public QueueTriggerBinding(IArgumentBinding<CloudQueueMessage> argumentBinding, CloudStorageAccount account,
            string queueName)
        {
            _argumentBinding = argumentBinding;
            _accountName = StorageClient.GetAccountName(account);
            _queueName = queueName;
        }

        public IReadOnlyDictionary<string, Type> BindingDataContract
        {
            get { return BindingData.GetContract(_argumentBinding.ValueType); }
        }

        public string QueueName
        {
            get { return _queueName; }
        }

        public ITriggerData Bind(CloudQueueMessage value, ArgumentBindingContext context)
        {
            IValueProvider valueProvider = _argumentBinding.Bind(value, context);
            IReadOnlyDictionary<string, object> bindingData = CreateBindingData(value);

            return new TriggerData(valueProvider, bindingData);
        }

        public ITriggerData Bind(object value, ArgumentBindingContext context)
        {
            CloudQueueMessage message = null;

            if (!_converter.TryConvert(value, out message))
            {
                throw new InvalidOperationException("Unable to convert trigger to CloudQueueMessage.");
            }

            return Bind(message, context);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new QueueTriggerParameterDescriptor
            {
                AccountName = _accountName,
                QueueName = _queueName
            };
        }

        private IReadOnlyDictionary<string, object> CreateBindingData(CloudQueueMessage value)
        {
            return BindingData.GetBindingData(value.AsString, BindingDataContract);
        }
    }
}
