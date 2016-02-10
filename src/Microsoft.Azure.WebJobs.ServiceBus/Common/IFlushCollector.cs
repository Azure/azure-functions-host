// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    // IAsyncCollector needs a Flush() to support batching calls and drain at the end. 
    internal interface IFlushCollector<T> : IAsyncCollector<T>
    {        
        // Flush all the events accumulated so far. 
        Task FlushAsync();
    }  
}