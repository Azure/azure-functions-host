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
            string queueName = "samples-input-nonexistent";
            bool exists = await Fixture.NamespaceManager.QueueExistsAsync(queueName);
            Assert.False(exists, $"This test expects the queue '{queueName}' to not exist, but it does.");

            IEnumerable<string> logs = null;

            await TestHelpers.Await(() =>
            {
                logs = GetTracesForFunction("ListenerStartupException");
                string logToFind = "The listener for function 'Functions.ListenerStartupException' was unable to start.";
                return logs.Any(l => l.Contains(logToFind));
            });

            // see how many Timer logs we've seen so far. We'll make sure we see more below.
            string timerLogToFind = "Timer function ran!";
            int timerCount = GetTracesForFunction("TimerTrigger").Count(p => p.Contains(timerLogToFind));

            // assert that timer function is still running
            await TestHelpers.Await(() =>
            {
                int newTimerCount = GetTracesForFunction("TimerTrigger").Count(p => p.Contains(timerLogToFind));
                return newTimerCount > timerCount;
            });

            // assert that the host is retrying to start the
            // listener in the background
            await TestHelpers.Await(() =>
            {
                logs = Fixture.TraceWriter.GetTraces().Select(p => p.Message);
                string logToFind = "Retrying to start listener for function 'Functions.ListenerStartupException' (Attempt 2)";
                return logs.Any(l => l.Contains(logToFind));
            });

            // assert that Stop does not throw error
            Fixture.Host.Stop();
        }

        private IEnumerable<string> GetTracesForFunction(string functionName)
        {
            return Fixture.TraceWriter.GetTraces()
                .Where(p =>
                {
                    return p.Properties.TryGetValue(ScriptConstants.TracePropertyFunctionNameKey, out string name) &&
                           name == functionName;
                })
                .Select(p => p.Message);
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
