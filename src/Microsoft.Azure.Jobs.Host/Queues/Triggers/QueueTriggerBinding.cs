using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Queues.Listeners;
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

        private readonly string _parameterName;
        private readonly IArgumentBinding<CloudQueueMessage> _argumentBinding;
        private readonly CloudStorageAccount _account;
        private readonly string _accountName;
        private readonly string _queueName;
        private readonly IReadOnlyDictionary<string, Type> _bindingDataContract;

        public QueueTriggerBinding(string parameterName, IArgumentBinding<CloudQueueMessage> argumentBinding,
            CloudStorageAccount account, string queueName)
        {
            _parameterName = parameterName;
            _argumentBinding = argumentBinding;
            _account = account;
            _accountName = StorageClient.GetAccountName(account);
            _queueName = queueName;
            _bindingDataContract = CreateBindingDataContract(argumentBinding.ValueType);
        }

        public IReadOnlyDictionary<string, Type> BindingDataContract
        {
            get { return _bindingDataContract; }
        }

        public string QueueName
        {
            get { return _queueName; }
        }

        private static IReadOnlyDictionary<string, Type> CreateBindingDataContract(Type valueType)
        {
            Dictionary<string, Type> contract = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            contract.Add("DequeueCount", typeof(int));
            contract.Add("ExpirationTime", typeof(DateTimeOffset));
            contract.Add("Id", typeof(string));
            contract.Add("InsertionTime", typeof(DateTimeOffset));
            contract.Add("NextVisibleTime", typeof(DateTimeOffset));
            contract.Add("PopReceipt", typeof(string));

            IReadOnlyDictionary<string, Type> contractFromValueType = BindingData.GetContract(valueType);

            if (contractFromValueType != null)
            {
                foreach (KeyValuePair<string, Type> item in contractFromValueType)
                {
                    // In case of conflict, binding data from the value type overrides the built-in binding data above.
                    contract[item.Key] = item.Value;
                }
            }

            return contract;
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

        public ITriggerClient CreateClient(MethodInfo method, IReadOnlyDictionary<string, IBinding> nonTriggerBindings,
            FunctionDescriptor functionDescriptor)
        {
            ITriggeredFunctionBinding<CloudQueueMessage> functionBinding =
                new TriggeredFunctionBinding<CloudQueueMessage>(method, _parameterName, this, nonTriggerBindings);
            CloudQueueClient client = _account.CreateCloudQueueClient();
            CloudQueue queue = client.GetQueueReference(_queueName);
            IListenerFactory listenerFactory = new QueueListenerFactory(queue, functionBinding, functionDescriptor,
                method);
            return new TriggerClient<CloudQueueMessage>(functionBinding, listenerFactory);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new QueueTriggerParameterDescriptor
            {
                Name = _parameterName,
                AccountName = _accountName,
                QueueName = _queueName
            };
        }

        private IReadOnlyDictionary<string, object> CreateBindingData(CloudQueueMessage value)
        {
            Dictionary<string, object> bindingData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            bindingData.Add("DequeueCount", value.DequeueCount);
            bindingData.Add("ExpirationTime", value.ExpirationTime.GetValueOrDefault(DateTimeOffset.MaxValue));
            bindingData.Add("Id", value.Id);
            bindingData.Add("InsertionTime", value.InsertionTime.GetValueOrDefault(DateTimeOffset.UtcNow));
            bindingData.Add("NextVisibleTime", value.NextVisibleTime.GetValueOrDefault(DateTimeOffset.MaxValue));
            bindingData.Add("PopReceipt", value.PopReceipt);
            
            IReadOnlyDictionary<string, object> bindingDataFromValueType = BindingData.GetBindingData(value.AsString,
                _bindingDataContract);

            if (bindingDataFromValueType != null)
            {
                foreach (KeyValuePair<string, object> item in bindingDataFromValueType)
                {
                    // In case of conflict, binding data from the value type overrides the built-in binding data above.
                    bindingData[item.Key] = item.Value;
                }
            }

            return bindingData;
        }
    }
}
