// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class FunctionsVersionLoggerServiceTests
    {
        private readonly FunctionsVersionLoggerService _monitor;
        private readonly TestEnvironment _environment;
        private readonly TestLoggerProvider _loggerProvider;

        public FunctionsVersionLoggerServiceTests()
        {
            _loggerProvider = new TestLoggerProvider();
            _environment = new TestEnvironment();
            ILoggerFactory loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);
            ILogger<FunctionsVersionLoggerService> logger = loggerFactory.CreateLogger<FunctionsVersionLoggerService>();
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsExtensionVersion, "~4");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.Framework, "dotnet");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.FrameworkVersion, "6");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSlotName, "development");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.KubernetesServiceHost, "KubernetesHost");
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.PodNamespace, "PodNs");
            _monitor = new FunctionsVersionLoggerService(logger, _environment);
        }

        [Fact]
        public async Task OnTimer_LogsPublished()
        {
            await _monitor.StartAsync(CancellationToken.None);

            // wait for a few failures to happen
            LogMessage[] logs = null;

            await TestHelpers.Await(() =>
            {
                logs = _loggerProvider.GetAllLogMessages().Where(p => p.Level == LogLevel.Information).ToArray();
                return logs.Length >= 1;
            });
            Assert.All(logs,
                p =>
                {
                    Assert.Equal(GetRespectiveMessage(p.FormattedMessage), p.FormattedMessage);
                });
        }

        private string GetRespectiveMessage(string msg)
        {
            switch (msg)
            {
                case "FunctionsExtensionVersion : ~4":
                    return msg;
                case "Framework : dotnet":
                    return msg;
                case "FrameworkVersion : 6":
                    return msg;
                case "SlotName : development":
                    return msg;
            }
            return "IncorrectMessage";
        }
    }
}
