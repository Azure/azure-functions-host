// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    // The core object we get when an EventHub is triggered. 
    // This gets converter to the user type (EventData, string, poco, etc) 
    internal sealed class EventHubTriggerInput      
    {        
        // If != -1, then only process a single event in this batch. 
        private int _selector = -1;

        internal EventData[] Events { get; set; }
        internal PartitionContext Context { get; set; }

        public static EventHubTriggerInput New(EventData eventData)
        {
            return new EventHubTriggerInput
            {
                Context = null,
                Events = new EventData[]
                {
                      eventData
                },
                _selector = 0,
            };
        }

        public EventHubTriggerInput GetSingleEventTriggerInput(int idx)
        {
            return new EventHubTriggerInput
            {
                Events = this.Events,
                Context = this.Context,
                _selector = idx
            };
        }

        public EventData GetSingleEventData()
        {
            return this.Events[this._selector];
        }        
    }
}