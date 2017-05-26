// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.EventHubs;
using Microsoft.Azure.EventHubs.Processor;
using Microsoft.Azure.ServiceBus;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    // The core object we get when an EventHub is triggered. 
    // This gets converted to the user type (EventData, string, poco, etc) 
    internal sealed class EventHubTriggerInput      
    {        
        // If != -1, then only process a single event in this batch. 
        private int _selector = -1;

        internal EventData[] Events { get; set; }
        internal PartitionContext PartitionContext { get; set; }

        public static EventHubTriggerInput New(EventData eventData)
        {
            return new EventHubTriggerInput
            {
                PartitionContext = null,
                Events = new EventData[]
                {
                      eventData
                },
                _selector = 0,
            };
        }

        public bool IsSingleDispatch
        {
            get
            {
                return _selector != -1;
            }
        }

        public EventHubTriggerInput GetSingleEventTriggerInput(int idx)
        {
            return new EventHubTriggerInput
            {
                Events = this.Events,
                PartitionContext = this.PartitionContext,
                _selector = idx
            };
        }

        public EventData GetSingleEventData()
        {
            return this.Events[this._selector];
        }        
    }
}