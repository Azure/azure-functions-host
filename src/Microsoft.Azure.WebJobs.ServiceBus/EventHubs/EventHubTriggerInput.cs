// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    // The core object we get when an EventHub is triggered. 
    // This gets converter to the user type (EventData, string, poco, etc) 
    internal sealed class EventHubTriggerInput      
    {
        internal EventData[] _events;
        internal PartitionContext _context;

        // If != -1, then only process a single event in this batch. 
        public int _selector = -1;

        public EventHubTriggerInput GetSingleEvent(int idx)
        {
            return new EventHubTriggerInput
            {
                _events = this._events,
                _context = this._context,
                _selector = idx
            };
        }     
    }
}