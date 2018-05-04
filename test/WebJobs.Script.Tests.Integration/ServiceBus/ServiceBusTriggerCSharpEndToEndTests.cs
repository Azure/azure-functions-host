// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.ServiceBus
{
    public class ServiceBusCSharpEndToEndTests :
        ServiceBusEndToEndTestsBase<ServiceBusCSharpEndToEndTests.TestFixture>
    {
        public ServiceBusCSharpEndToEndTests(TestFixture fixture) : base(fixture)
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
            private const string ScriptRoot = @"TestScripts\CSharp";

            public TestFixture() : base(ScriptRoot, "csharp")
            {
            }
        }
    }
}
