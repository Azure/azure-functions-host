// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
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

                string logToFind = "The listener for function 'Functions.ListenerStartupException' was unable to start.";
                return logs.Any(l => l.Contains(logToFind));
            });

            TestHelpers.ClearFunctionLogs("TimerTrigger");

            // assert that timer function is still running
            await TestHelpers.Await(() =>
            {
                logs = TestHelpers.GetFunctionLogsAsync("TimerTrigger", false).Result;

                string logToFind = "Timer function ran!";
                return logs.Any(l => l.Contains(logToFind));
            });

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
            }
        }
    }
}
