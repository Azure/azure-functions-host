// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Queues.Listeners;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Triggers
{
    internal class QueueTriggerBinding : ITriggerBinding<IStorageQueueMessage>
    {
        private readonly string _parameterName;
        private readonly IStorageQueue _queue;
        private readonly ITriggerDataArgumentBinding<IStorageQueueMessage> _argumentBinding;
        private readonly IReadOnlyDictionary<string, Type> _bindingDataContract;
        private readonly IObjectToTypeConverter<IStorageQueueMessage> _converter;

        public QueueTriggerBinding(string parameterName, IStorageQueue queue,
            ITriggerDataArgumentBinding<IStorageQueueMessage> argumentBinding)
        {
            _parameterName = parameterName;
            _queue = queue;
            _argumentBinding = argumentBinding;
            _bindingDataContract = CreateBindingDataContract(argumentBinding);
            _converter = CreateConverter(queue);
        }

        public IReadOnlyDictionary<string, Type> BindingDataContract
        {
            get { return _bindingDataContract; }
        }

        public string QueueName
        {
            get { return _queue.Name; }
        }

        private static IReadOnlyDictionary<string, Type> CreateBindingDataContract(
            ITriggerDataArgumentBinding<IStorageQueueMessage> argumentBinding)
        {
            Dictionary<string, Type> contract = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            contract.Add("QueueTrigger", typeof(string));
            contract.Add("DequeueCount", typeof(int));
            contract.Add("ExpirationTime", typeof(DateTimeOffset));
            contract.Add("Id", typeof(string));
            contract.Add("InsertionTime", typeof(DateTimeOffset));
            contract.Add("NextVisibleTime", typeof(DateTimeOffset));
            contract.Add("PopReceipt", typeof(string));

            if (argumentBinding.BindingDataContract != null)
            {
                foreach (KeyValuePair<string, Type> item in argumentBinding.BindingDataContract)
                {
                    // In case of conflict, binding data from the value type overrides the built-in binding data above.
                    contract[item.Key] = item.Value;
                }
            }

            return contract;
        }

        private static IObjectToTypeConverter<IStorageQueueMessage> CreateConverter(IStorageQueue queue)
        {
            return new CompositeObjectToTypeConverter<IStorageQueueMessage>(
                new OutputConverter<CloudQueueMessage>(new CloudQueueMessageToStorageQueueMessageConverter()),
                new OutputConverter<string>(new StringToStorageQueueMessageConverter(queue)));
        }

        public async Task<ITriggerData> BindAsync(IStorageQueueMessage value, ValueBindingContext context)
        {
            ITriggerData triggerData = await _argumentBinding.BindAsync(value, context);
            IReadOnlyDictionary<string, object> bindingData = CreateBindingData(value, triggerData.BindingData);

            return new TriggerData(triggerData.ValueProvider, bindingData);
        }

        public Task<ITriggerData> BindAsync(object value, ValueBindingContext context)
        {
            IStorageQueueMessage message = null;

            if (!_converter.TryConvert(value, out message))
            {
                throw new InvalidOperationException("Unable to convert trigger to IStorageQueueMessage.");
            }

            return BindAsync(message, context);
        }

        public IFunctionDefinition CreateFunctionDefinition(IReadOnlyDictionary<string, IBinding> nonTriggerBindings,
            IInvoker invoker, FunctionDescriptor functionDescriptor)
        {
            ITriggeredFunctionBinding<IStorageQueueMessage> functionBinding =
                new TriggeredFunctionBinding<IStorageQueueMessage>(_parameterName, this, nonTriggerBindings);
            ITriggeredFunctionInstanceFactory<IStorageQueueMessage> instanceFactory =
                new TriggeredFunctionInstanceFactory<IStorageQueueMessage>(functionBinding, invoker,
                    functionDescriptor);
            IListenerFactory listenerFactory = new QueueListenerFactory(_queue, instanceFactory);
            return new FunctionDefinition(instanceFactory, listenerFactory);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new QueueTriggerParameterDescriptor
            {
                Name = _parameterName,
                AccountName = QueueClient.GetAccountName(_queue.ServiceClient),
                QueueName = _queue.Name
            };
        }

        private IReadOnlyDictionary<string, object> CreateBindingData(IStorageQueueMessage value,
            IReadOnlyDictionary<string, object> bindingDataFromValueType)
        {
            Dictionary<string, object> bindingData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            string queueMessageString = value.TryGetAsString();

            // Don't provide the QueueTrigger binding data when the queue message is not a valid string.
            if (queueMessageString != null)
            {
                bindingData.Add("QueueTrigger", queueMessageString);
            }

            bindingData.Add("DequeueCount", value.DequeueCount);
            bindingData.Add("ExpirationTime", value.ExpirationTime.GetValueOrDefault(DateTimeOffset.MaxValue));
            bindingData.Add("Id", value.Id);
            bindingData.Add("InsertionTime", value.InsertionTime.GetValueOrDefault(DateTimeOffset.UtcNow));
            bindingData.Add("NextVisibleTime", value.NextVisibleTime.GetValueOrDefault(DateTimeOffset.MaxValue));
            bindingData.Add("PopReceipt", value.PopReceipt);
            
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
