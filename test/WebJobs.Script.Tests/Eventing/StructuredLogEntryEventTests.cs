// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Eventing
{
    public class StructuredLogEntryEventTests
    {
        [Fact]
        public void WhenEventsAreNotConsumed_FactoryIsNotInvoked()
        {
            bool factoryInvoked = false;
            Func<StructuredLogEntry> factory = () =>
            {
                factoryInvoked = true;
                return new StructuredLogEntry("test");
            };

            var logEntryEvent = new StructuredLogEntryEvent(factory);

            Assert.False(factoryInvoked);
        }
    }
}
