// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.


namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    // Fake queue message. Equivalent of CloudQueueMessage or EventData
    public class FakeQueueData
    {
        // This corresponds to string & poco conversion. 
        public string Message { get; set; }

        // Advanced property not captured with JSON serialization. 
        public string ExtraPropertery { get; set; }

        // For testing passing bytes. 
        public byte Byte { get; set; }
    }
}