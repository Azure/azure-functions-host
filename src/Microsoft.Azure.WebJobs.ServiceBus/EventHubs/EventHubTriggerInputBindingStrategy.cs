// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.ServiceBus.Messaging;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    // Binding strategy for an event hub triggers. 
    class EventHubTriggerBindingStrategy : ITriggerBindingStrategy<EventData, EventHubTriggerInput>
    {
        public EventHubTriggerInput ConvertFromString(string x)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(x);

            // Return a single event. Doesn't support multiple dispatch 
            return new EventHubTriggerInput
            {
                 _context = null,
                  _events = new EventData[] {
                      new EventData(bytes)
                  },
                 _selector = 0,
            };
        }

        const string DataContract_PartitionContext = "partitionContext";

        // Get the static binding contract
        //  - gets augmented 
        public Dictionary<string, Type> GetStaticBindingContract()
        {
            Dictionary<string, Type> contract = new Dictionary<string, Type>();
            contract[DataContract_PartitionContext] = typeof(PartitionContext);
            return contract;
        }

        // Single instance: Core --> EventData
        public EventData BindMessage(EventHubTriggerInput value, ValueBindingContext context)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value");
            }
            EventData eventData = value._events[value._selector];
            return eventData;
        }

        public EventData[] BindMessageArray(EventHubTriggerInput value, ValueBindingContext context)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value");
            }
            return value._events;
        }

        // GEt runtime instance of binding contract 
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "PartitionContext")]
        public Dictionary<string, object> GetContractInstance(EventHubTriggerInput value)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value");
            }
            if (value._context == null)
            {
                throw new InvalidOperationException("Missing PartitionContext");
            }
            Dictionary<string, object> bindingData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            bindingData[DataContract_PartitionContext] = value._context;
            return bindingData;
        }
    }
}