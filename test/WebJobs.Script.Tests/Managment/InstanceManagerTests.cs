// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Managment
{
    public class InstanceManagerTests : IDisposable
    {
        [Fact]
        public async Task StartAssignmentShouldUpdateEnvironmentVariables()
        {
            var loggerFactory = MockNullLogerFactory.CreateLoggerFactory();
            var scriptHostManager = new Mock<WebScriptHostManager>(new ScriptHostConfiguration(),
                Mock.Of<ISecretManagerFactory>(),
                Mock.Of<IScriptEventManager>(),
                new ScriptSettingsManager(),
                new WebHostSettings { SecretsPath = Path.GetTempPath() },
                Mock.Of<IWebJobsRouter>(),
                loggerFactory,
                null, null, null, null, null, 30, 500);

            var scriptSettingManager = new Mock<ScriptSettingsManager>();
            var restartCalled = false;
            var resetCalled = false;

            scriptHostManager
                .Setup(m => m.RestartHost())
                .Callback(() => restartCalled = true);

            scriptSettingManager
                .Setup(m => m.Reset())
                .Callback(() => resetCalled = true);

            ScriptSettingsManager.Instance = scriptSettingManager.Object;
            var instanceManager = new InstanceManager(scriptHostManager.Object, null, loggerFactory, null);

            var envValue = new
            {
                Name = Path.GetTempFileName().Replace(".", string.Empty),
                Value = Guid.NewGuid().ToString()
            };

            instanceManager.StartAssignment(new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>
                {
                    { envValue.Name, envValue.Value }
                }
            });

            // TryAssign does the specialization in the background
            await Task.Delay(500);

            Assert.True(restartCalled, userMessage: "calling assign should call restart on the host");
            Assert.True(resetCalled, userMessage: "calling assign should call reset on the settings manager");
            var value = Environment.GetEnvironmentVariable(envValue.Name);
            Assert.Equal(value, envValue.Value);
        }

        public void Dispose()
        {
            // Clean up
            // Reset ScriptSettingsManager.Instance
            ScriptSettingsManager.Instance = new ScriptSettingsManager();
        }
    }
}
