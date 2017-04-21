// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Description;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    /// <summary>
    /// Setup an 'output' binding to an EventHub. This can be any output type compatible with an IAsyncCollector.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
    [Binding]
    public sealed class EventHubAttribute : Attribute
    {
        /// <summary>
        /// Initialize a new instance of the <see cref="EventHubAttribute"/>
        /// </summary>
        /// <param name="eventHubName">Name of the event hub as resolved against the <see cref="EventHubConfiguration"/> </param>
        public EventHubAttribute(string eventHubName)
        {
            this.EventHubName = eventHubName;
        }

        /// <summary>
        /// The name of the event hub. This is resolved against the <see cref="EventHubConfiguration"/>
        /// </summary>
        [AutoResolve]
        public string EventHubName { get; private set; }
    }    
}