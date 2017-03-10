// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ListenerFailureTests : EndToEndTestsBase<ListenerFailureTests.TestFixture>
    {
        public ListenerFailureTests(TestFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task ListenerError_LogsAndDoesNotStopHost()
        {
            IList<string> logs = null;

            await TestHelpers.Await(() =>
            {
                logs = TestHelpers.GetFunctionLogsAsync("ListenerStartupException", false).Result;
                return logs.Count > 0;
            });

            // assert that listener error was captured
            Assert.Contains(logs, (log) => log.Contains("The listener for function 'Functions.ListenerStartupException' was unable to start."));

            TestHelpers.ClearFunctionLogs("TimerTrigger");
            await TestHelpers.Await(() =>
            {
                logs = TestHelpers.GetFunctionLogsAsync("TimerTrigger", false).Result;
                return logs.Count > 0;
            });

            // assert that timer function is still running
            Assert.Contains(logs, (log) => log.Contains("Timer function ran!"));

            // assert that Stop does not throw error
            Fixture.Host.Stop();
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\ListenerExceptions", "listeners")
            {
            }

            public override void Dispose()
            {
                // host should already be stopped from test
                try
                {
                    Host.Stop();
                }
                catch
                {
                }
                Host.Dispose();
                ServiceBusQueueClient.Close();
            }
        }
    }
}
