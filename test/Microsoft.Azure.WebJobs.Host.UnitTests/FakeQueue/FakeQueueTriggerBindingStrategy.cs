// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Triggers;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    // Strategy object for binding triggers to the various permutations. 
    internal class FakeQueueTriggerBindingStrategy : ITriggerBindingStrategy<FakeQueueData, FakeQueueDataBatch>
    {
        public FakeQueueData BindSingle(FakeQueueDataBatch value, ValueBindingContext context)
        {
            return value.Events[0];
        }

        public FakeQueueData[] BindMultiple(FakeQueueDataBatch value, ValueBindingContext context)
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