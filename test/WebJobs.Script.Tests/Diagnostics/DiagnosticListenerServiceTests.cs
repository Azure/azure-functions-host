// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using static Microsoft.Azure.WebJobs.Script.ScriptConstants;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class DiagnosticListenerServiceTests
    {
        /// <summary>
        /// This test ensures that:
        ///     1 - Diagnostic sources with the host prefix is correctly observed
        ///     2 - Diagnostic sources with names not starting with the host prefix
        ///         are not observed.
        /// </summary>
        [Fact]
        public async Task CreatesExpectedListeners()
        {
            var logger = new TestLogger<DiagnosticListenerService>();
            var om = new Mock<IOptionsMonitor<StandbyOptions>>();
            om.Setup(m => m.CurrentValue).Returns(new StandbyOptions());

            var service = new DiagnosticListenerService(logger, om.Object);

            await service.StartAsync(CancellationToken.None);

            // Host diagnostic source
            using var source = new DiagnosticListener(HostDiagnosticSourcePrefix + "Test");
            source.Write("TestEvent", "test");

            // Diagnostic source without hosts prefix
            using var source2 = new DiagnosticListener("OtherSource.Test");
            source2.Write("TestEvent2", "test");

            Assert.Contains(GetEventMessge(source.Name, "TestEvent", "test"), logger.GetLogMessages().Select(l => l.FormattedMessage));

            // This assertion accounts for the log message written when a listener handler is created.
            Assert.Equal(2, GetMessageCount(logger.GetLogMessages(), source.Name));
        }

        /// <summary>
        /// This test ensures that enablement evaluation for events starting wiht the debug prefix
        /// return false if debug tracing is not enabled.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CapturesDebugEvents_WhenDebugTracesAreEnabled(bool debugTracingEnabled)
        {
            TestScopedEnvironmentVariable featureFlags = null;

            try
            {
                if (debugTracingEnabled)
                {
                    featureFlags = new TestScopedEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, FeatureFlagEnableDebugTracing);
                }

                var logger = new TestLogger<DiagnosticListenerService>();
                var om = new Mock<IOptionsMonitor<StandbyOptions>>();
                om.Setup(m => m.CurrentValue).Returns(new StandbyOptions());

                var service = new DiagnosticListenerService(logger, om.Object);

                await service.StartAsync(CancellationToken.None);

                // Host diagnostic source
                using var source = new DiagnosticListener(HostDiagnosticSourcePrefix + "Test");

                // Debug event. This will only be captured if debug tracing is enabled:
                var eventName = HostDiagnosticSourceDebugEventNamePrefix + "TestEvent";
                if (source.IsEnabled(eventName))
                {
                    source.Write(eventName, "test debug");
                }

                // This event should always be captured
                source.Write("TestEvent", "test");

                var messages = logger.GetLogMessages().Select(l => l.FormattedMessage).ToList();

                Assert.Contains(GetEventMessge(source.Name, "TestEvent", "test"), messages);

                if (debugTracingEnabled)
                {
                    Assert.Contains(GetEventMessge(source.Name, eventName, "test debug"), messages);
                }

                int expectedMessageCount = debugTracingEnabled ? 3 : 2;
                // This assertion accounts for the log message written when a listener handler is created.
                Assert.Equal(expectedMessageCount, GetMessageCount(logger.GetLogMessages(), source.Name));

                Assert.Equal(debugTracingEnabled, source.IsEnabled(eventName));
            }
            finally
            {
                featureFlags?.Dispose();
            }
        }

        /// <summary>
        /// This test ensures that specialization events trigger evaluation of the debug flag and source subscription.
        /// </summary>
        [Fact]
        public async Task StandbyChanges_TriggerFeatureEvaluation()
        {
            Action<StandbyOptions, string> action = null;
            void ChangeCallback(Action<StandbyOptions, string> callback)
            {
                action = callback;
            }

            var logger = new TestLogger<DiagnosticListenerService>();
            var om = new Mock<IOptionsMonitor<StandbyOptions>>();
            om.Setup(m => m.CurrentValue).Returns(new StandbyOptions { InStandbyMode = true });
            om.Setup(m => m.OnChange(It.IsAny<Action<StandbyOptions, string>>()))
                .Callback(ChangeCallback);

            var service = new DiagnosticListenerService(logger, om.Object);

            await service.StartAsync(CancellationToken.None);

            // Host diagnostic source
            using var source = new DiagnosticListener(HostDiagnosticSourcePrefix + "Test");

            // Debug event. This will only be captured if debug tracing is enabled:
            var eventName = HostDiagnosticSourceDebugEventNamePrefix + "TestEvent";

            Assert.False(source.IsEnabled(eventName));

            using var featureFlags = new TestScopedEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, FeatureFlagEnableDebugTracing);

            // Invoke change callback
            action(new StandbyOptions(), string.Empty);

            Assert.True(source.IsEnabled(eventName));
        }

        private static string GetEventMessge(string source, string eventName, string payload)
            => $"Diagnostic source '{source}' emitted event '{eventName}': {payload}";

        private static int GetMessageCount(IList<LogMessage> logs, string source)
        {
            // Exclude all the sources that's not relevant for this test
            int count = 0;
            foreach (var item in logs)
            {
                if (item.FormattedMessage.Contains(source))
                {
                    count++;
                }
            }
            return count;
        }
    }
}
