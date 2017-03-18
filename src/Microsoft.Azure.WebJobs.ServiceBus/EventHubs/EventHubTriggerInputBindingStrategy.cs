// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    // Binding strategy for an event hub triggers. 
    internal class EventHubTriggerBindingStrategy : ITriggerBindingStrategy<EventData, EventHubTriggerInput>
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public EventHubTriggerInput ConvertFromString(string input)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            EventData eventData = new EventData(bytes);

            // Return a single event. Doesn't support multiple dispatch 
            return EventHubTriggerInput.New(eventData);            
        }

        // Single instance: Core --> EventData
        public EventData BindSingle(EventHubTriggerInput value, ValueBindingContext context)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value");
            }
            return value.GetSingleEventData();
        }

        public EventData[] BindMultiple(EventHubTriggerInput value, ValueBindingContext context)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value");
            }
            return value.Events;
        }

        // Get the static binding contract
        //  - gets augmented 
        public Dictionary<string, Type> GetStaticBindingContract()
        {
            // TODO: need to be able to determine here if we're single dispatch
            // https://github.com/Azure/azure-webjobs-sdk/issues/1072
            bool isSingleDispatch = true;

            return GetBindingContract(isSingleDispatch);
        }

        internal static Dictionary<string, Type> GetBindingContract(bool isSingleDispatch)
        {
            Dictionary<string, Type> contract = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            contract.Add("PartitionContext", typeof(PartitionContext));

            // TODO: need to be able to determine here if we're single dispatch
            // https://github.com/Azure/azure-webjobs-sdk/issues/1072
            if (isSingleDispatch)
            {
                AddBindingContractMember(contract, "PartitionKey", typeof(string), isSingleDispatch);
                AddBindingContractMember(contract, "Offset", typeof(string), isSingleDispatch);
                AddBindingContractMember(contract, "SequenceNumber", typeof(long), isSingleDispatch);
                AddBindingContractMember(contract, "EnqueuedTimeUtc", typeof(DateTime), isSingleDispatch);
                AddBindingContractMember(contract, "Properties", typeof(IDictionary<string, object>), isSingleDispatch);
                AddBindingContractMember(contract, "SystemProperties", typeof(IDictionary<string, object>), isSingleDispatch);
            }

            return contract;
        }

        private static void AddBindingContractMember(Dictionary<string, Type> contract, string name, Type type, bool isSingleDispatch)
        {
            contract.Add(name, isSingleDispatch ? type : type.MakeArrayType());
        }

        // Get runtime instance of binding contract 
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "PartitionContext")]
        public Dictionary<string, object> GetContractInstance(EventHubTriggerInput value)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value");
            }

            return GetBindingData(value);
        }

        internal static Dictionary<string, object> GetBindingData(EventHubTriggerInput input)
        {
            var bindingData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            SafeAddValue(() => bindingData.Add(nameof(input.PartitionContext), input.PartitionContext));

            if (input.IsSingleDispatch)
            {
                AddBindingData(bindingData, input.GetSingleEventData());
            }
            else
            {
                // TODO: handle multiple dispatch
                // https://github.com/Azure/azure-webjobs-sdk/issues/1072
            }

            return bindingData;
        }

        private static void AddBindingData(Dictionary<string, object> bindingData, EventData eventData)
        {
            SafeAddValue(() => bindingData.Add(nameof(eventData.PartitionKey), eventData.PartitionKey));
            SafeAddValue(() => bindingData.Add(nameof(eventData.Offset), eventData.Offset));
            SafeAddValue(() => bindingData.Add(nameof(eventData.SequenceNumber), eventData.SequenceNumber));
            SafeAddValue(() => bindingData.Add(nameof(eventData.EnqueuedTimeUtc), eventData.EnqueuedTimeUtc));
            SafeAddValue(() => bindingData.Add(nameof(eventData.Properties), eventData.Properties));
            SafeAddValue(() => bindingData.Add(nameof(eventData.SystemProperties), eventData.SystemProperties));
        }

        private static void SafeAddValue(Action addValue)
        {
            try
            {
                addValue();
            }
            catch
            {
                // some message propery getters can throw, based on the
                // state of the message
            }
        }
    }
}