// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    // Binding strategy for an event hub triggers. 
    internal class EventHubTriggerBindingStrategy : ITriggerBindingStrategy<EventData, EventHubTriggerInput>
    {
        private const string DataContractPartitionContext = "partitionContext";

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public EventHubTriggerInput ConvertFromString(string x)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(x);
            EventData eventData = new EventData(bytes);

            // Return a single event. Doesn't support multiple dispatch 
            return EventHubTriggerInput.New(eventData);            
        }

        // Get the static binding contract
        //  - gets augmented 
        public Dictionary<string, Type> GetStaticBindingContract()
        {
            Dictionary<string, Type> contract = new Dictionary<string, Type>();
            contract[DataContractPartitionContext] = typeof(PartitionContext);
            return contract;
        }

        // Single instance: Core --> EventData
        public EventData BindMessage(EventHubTriggerInput value, ValueBindingContext context)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value");
            }
            EventData eventData = value.GetSingleEventData();
            return eventData;
        }

        public EventData[] BindMessageArray(EventHubTriggerInput value, ValueBindingContext context)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value");
            }
            return value.Events;
        }

        // GEt runtime instance of binding contract 
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "PartitionContext")]
        public Dictionary<string, object> GetContractInstance(EventHubTriggerInput value)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value");
            }
            if (value.Context == null)
            {
                throw new InvalidOperationException("Missing PartitionContext");
            }
            Dictionary<string, object> bindingData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            bindingData[DataContractPartitionContext] = value.Context;
            return bindingData;
        }
    }
}