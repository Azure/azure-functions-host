// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Bindings;
using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    // Strategy pattern to describe how to bind a core trigger type to various parameter.
    // Suports:
    // - both Single-item vs. Batch dispatch.  
    // - core binding, string, poco w/ Binding Contracts
    // 
    // For example, a single EventHubTriggerInput -->  can bind to 
    //  EventData, EventData[], string, string[], Poco, Poco[]    
    interface ITriggerBindingStrategy<TMessage, TTriggerValue>
    {        
        // Given a raw string, convert to a TTriggerValue.
        // This is primarily used in the "invoke from dashboard" path. 
        TTriggerValue ConvertFromString(string message);

        // Get the static route-parameter contract for the TMessage. 
        // For example, if we bind a queue message to a POCO, 
        // then the properties on the Poco's type are route parameters that can feed into other bindings. 
        // Intentionally make this mutable so that callers can add more items to it and override defaults. 
        Dictionary<string, Type> GetStaticBindingContract();

        // Get the values of the route-parameters given an instance of the trigger value. 
        // This should match the strucutre in GetStaticBindingContract. 
        // Intentionally make this mutable so that callers can add more items to it. 
        Dictionary<string, object> GetContractInstance(TTriggerValue value);

        // Bind as a single-item dispatch. 
        TMessage BindMessage(TTriggerValue value, ValueBindingContext context);

        // Bind as a multiple-item dispatch. 
        TMessage[] BindMessageArray(TTriggerValue value, ValueBindingContext context);
    }
}