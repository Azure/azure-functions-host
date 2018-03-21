// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.ServiceBus
{
    public class ServiceBusNodeEndToEndTests :
        ServiceBusEndToEndTestsBase<ServiceBusNodeEndToEndTests.TestFixture>
    {
        public ServiceBusNodeEndToEndTests(TestFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public Task ServiceBusQueueTriggerAndOutput()
        {
            return ServiceBusQueueTriggerAndOutputTest();
        }

        [Fact]
        public Task ServiceBusTopicTrigger()
        {
            return ServiceBusTopicTriggerTest();
        }

        [Fact]
        public Task ServiceBusTopicOutput()
        {
            return ServiceBusTopicOutputTest();
        }

        public class TestFixture : ServiceBusTestFixture
        {
            private const string ScriptRoot = @"TestScripts\Node";

            public TestFixture() : base(ScriptRoot, "node")
            {
            }
        }
    }
}
