// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.EventHubs.Processor;

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

        public Dictionary<string, Type> GetBindingContract(bool isSingleDispatch = true)
        {
            var contract = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            contract.Add("PartitionContext", typeof(PartitionContext));

            AddBindingContractMember(contract, "PartitionKey", typeof(string), isSingleDispatch);
            AddBindingContractMember(contract, "Offset", typeof(string), isSingleDispatch);
            AddBindingContractMember(contract, "SequenceNumber", typeof(long), isSingleDispatch);
            AddBindingContractMember(contract, "EnqueuedTimeUtc", typeof(DateTime), isSingleDispatch);
            AddBindingContractMember(contract, "Properties", typeof(IDictionary<string, object>), isSingleDispatch);
            AddBindingContractMember(contract, "SystemProperties", typeof(IDictionary<string, object>), isSingleDispatch);

            return contract;
        }

        private static void AddBindingContractMember(Dictionary<string, Type> contract, string name, Type type, bool isSingleDispatch)
        {
            if (!isSingleDispatch)
            {
                name += "Array";
            }
            contract.Add(name, isSingleDispatch ? type : type.MakeArrayType());
        }

        public Dictionary<string, object> GetBindingData(EventHubTriggerInput value)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value");
            }

            var bindingData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            SafeAddValue(() => bindingData.Add(nameof(value.PartitionContext), value.PartitionContext));

            if (value.IsSingleDispatch)
            {
                AddBindingData(bindingData, value.GetSingleEventData());
            }
            else
            {
                AddBindingData(bindingData, value.Events);
            }

            return bindingData;
        }

        internal static void AddBindingData(Dictionary<string, object> bindingData, EventData[] events)
        {
            int length = events.Length;
            var partitionKeys = new string[length];
            var offsets = new string[length];
            var sequenceNumbers = new long[length];
            var enqueuedTimesUtc = new DateTime[length];
            var properties = new IDictionary<string, object>[length];
            var systemProperties = new IDictionary<string, object>[length];

            SafeAddValue(() => bindingData.Add("PartitionKeyArray", partitionKeys));
            SafeAddValue(() => bindingData.Add("OffsetArray", offsets));
            SafeAddValue(() => bindingData.Add("SequenceNumberArray", sequenceNumbers));
            SafeAddValue(() => bindingData.Add("EnqueuedTimeUtcArray", enqueuedTimesUtc));
            SafeAddValue(() => bindingData.Add("PropertiesArray", properties));
            SafeAddValue(() => bindingData.Add("SystemPropertiesArray", systemProperties));

            for (int i = 0; i < events.Length; i++)
            {
                partitionKeys[i] = events[i].PartitionKey;
                offsets[i] = events[i].Offset;
                sequenceNumbers[i] = events[i].SequenceNumber;
                enqueuedTimesUtc[i] = events[i].EnqueuedTimeUtc;
                properties[i] = events[i].Properties;
                systemProperties[i] = events[i].SystemProperties;
            }
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