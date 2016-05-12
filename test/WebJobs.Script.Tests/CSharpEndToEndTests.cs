// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class CSharpEndToEndTests : EndToEndTestsBase<CSharpEndToEndTests.TestFixture>
    {
        private const string JobLogTestFileName = "joblog.txt";

        public CSharpEndToEndTests(TestFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task ServiceBusQueueTriggerToBlobTest()
        {
            await ServiceBusQueueTriggerToBlobTestImpl();
        }

        [Fact]
        public async Task MobileTables()
        {
            await MobileTablesTest(isCSharp: true);
        }

        [Fact]
        public async Task DocumentDB()
        {
            await DocumentDBTest();
        }

        [Fact]
        public async Task NotificationHub()
        {
            await NotificationHubTest("NotificationHubOut");
        }

        [Fact]
        public async Task NotificationHub_Out_Notification()
        {
            await NotificationHubTest("NotificationHubOutNotification");
        }

        [Fact]
        public async Task MobileTablesTable()
        {
            var id = Guid.NewGuid().ToString();
            Dictionary<string, object> arguments = new Dictionary<string, object>()
            {
                { "input",  id }
            };

            await Fixture.Host.CallAsync("MobileTableTable", arguments);

            await WaitForMobileTableRecordAsync("Item", id);
        }

        [Fact]
        public async Task ScriptReference_LoadsScript()
        {
            var request = new System.Net.Http.HttpRequestMessage();
            Dictionary<string, object> arguments = new Dictionary<string, object>()
            {
                { "req", request }
            };

            await Fixture.Host.CallAsync("LoadScriptReference", arguments);

            Assert.Equal("TestClass", request.Properties["LoadedScriptResponse"]);
        }

        [Fact]
        public async Task ApiHub()
        {
            await ApiHubTest();
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\CSharp", "csharp")
            {
                File.Delete(JobLogTestFileName);
            }
        }
    }
}
