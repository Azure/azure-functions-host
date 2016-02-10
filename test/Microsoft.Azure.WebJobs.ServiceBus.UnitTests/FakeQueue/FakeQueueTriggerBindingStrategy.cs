// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Bindings;
using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.ServiceBus.UnitTests
{
    // Strategy object for binding triggers to the various permutations. 
    internal class FakeQueueTriggerBindingStrategy : ITriggerBindingStrategy<FakeQueueData, FakeQueueDataBatch>
    {
        public FakeQueueData BindMessage(FakeQueueDataBatch value, ValueBindingContext context)
        {
            return value.Events[0];
        }

        public FakeQueueData[] BindMessageArray(FakeQueueDataBatch value, ValueBindingContext context)
        {
            return value.Events;
        }


        public FakeQueueDataBatch ConvertFromString(string x)
        {
            return new FakeQueueDataBatch
            {
                Events = new FakeQueueData[] {
                     new FakeQueueData { Message = x }
                 }
            };
        }

        public Dictionary<string, object> GetContractInstance(FakeQueueDataBatch value)
        {
            return new Dictionary<string, object>();
        }

        public Dictionary<string, Type> GetStaticBindingContract()
        {
            // No contract exposed
            return new Dictionary<string, Type>();
        }
    }
}