// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Bindings;


namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    // "Fake Queue" support for 100% in-memory for unit test bindings. 
    // Put on a parameter to mark that it goes to a "FakeQueue". 
    public class FakeQueueAttribute : Attribute, IAttributeInvokeDescriptor<FakeQueueAttribute>
    {
        [AutoResolve]
        public string Prefix { get; set; }

        public string ToInvokeString()
        {
            return this.Prefix;
        }
        public FakeQueueAttribute FromInvokeString(string invokeString)
        {
            return new FakeQueueAttribute { Prefix = invokeString };
        }
    }
}