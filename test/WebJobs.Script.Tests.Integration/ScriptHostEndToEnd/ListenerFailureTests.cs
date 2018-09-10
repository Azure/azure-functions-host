// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ListenerFailureTests : IClassFixture<ListenerFailureTests.TestFixture>
    {
        private TestFixture _fixture;

        public ListenerFailureTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        /// <summary>
        /// This test uses an EventHub triggered function that references an
        /// invalid hub name.
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task ListenerError_LogsAndDoesNotStopHost()
        {
            string[] logs = null;

            await TestHelpers.Await(() =>
            {
                logs = _fixture.Host.GetLogMessages().Select(p => p.FormattedMessage).Where(p => p != null).ToArray();

                string logToFind = "The listener for function 'Functions.ListenerStartupException' was unable to start.";
                return logs.Any(l => l.Contains(logToFind));
            });

            // assert that timer function is still running
            await TestHelpers.Await(() =>
            {
                logs = _fixture.Host.GetLogMessages().Select(p => p.FormattedMessage).Where(p => p != null).ToArray();

                string logToFind = "Timer function ran!";
                return logs.Any(l => l.Contains(logToFind));
            });

            // assert that the host is retrying to start the
            // listener in the background
            await TestHelpers.Await(() =>
            {
                logs = _fixture.Host.GetLogMessages().Select(p => p.FormattedMessage).Where(p => p != null).ToArray();
                string logToFind = "Retrying to start listener for function 'Functions.ListenerStartupException' (Attempt 2)";
                return logs.Any(l => l.Contains(logToFind));
            });
        }

        public class TestFixture : EndToEndTestFixture
        {
            private const string ScriptRoot = @"TestScripts\ListenerExceptions";

            public TestFixture() : base(ScriptRoot, "node", "Microsoft.Azure.WebJobs.Extensions.EventHubs", "3.0.0-rc*")
            {
            }

            protected override Task CreateTestStorageEntities()
            {
                return Task.CompletedTask;
            }
        }
    }
}