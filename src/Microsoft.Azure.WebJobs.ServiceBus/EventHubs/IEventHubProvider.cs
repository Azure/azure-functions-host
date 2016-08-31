// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    // Expose to binders / attributes so they can get the EventHub connections. 
    internal interface IEventHubProvider
    {
        // Lookup a listener for receiving events given the name provided in the [EventHubTrigger] attribute. 
        EventProcessorHost GetEventProcessorHost(string eventHubName, string consumerGroup);

        // Get the eventhub options, used by the EventHub SDK for listening on event. 
        EventProcessorOptions GetOptions();
    }
}