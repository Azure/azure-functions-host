// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.ServiceBus.UnitTests
{
    // "Fake Queue" support for 100% in-memory for unit test bindings. 
    // Put on a parameter to mark that it goes to a "FakeQueue". 
    public class FakeQueueAttribute : Attribute
    {
    }
}