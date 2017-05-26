// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.


namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    // The TTriggerValue for "Fake queues". 
    // this is the internal-native type that triggering fake queue messages bind to.
    // From this core type, binders will bind it to the various permutations. 
    public class FakeQueueDataBatch
    {
        public FakeQueueData[] Events;
    }
}