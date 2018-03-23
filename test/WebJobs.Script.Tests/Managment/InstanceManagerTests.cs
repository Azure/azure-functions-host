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
using NSubstitute;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Managment
{
    public class InstanceManagerTests : IDisposable
    {
        [Fact]
        public async Task TryAssignShouldUpdateEnvironmentVariables()
        {
            var loggerFactory = MockNullLogerFactory.CreateLoggerFactory();
            var scriptHostManager = Substitute.For<WebScriptHostManager>(new ScriptHostConfiguration(),
                Substitute.For<ISecretManagerFactory>(),
                Substitute.For<IScriptEventManager>(),
                new ScriptSettingsManager(),
                new WebHostSettings { SecretsPath = Path.GetTempPath() },
                Substitute.For<IWebJobsRouter>(),
                loggerFactory,
                null, null, null, null, null, 30, 500);

            var scriptSettingManager = Substitute.For<ScriptSettingsManager>();
            var restartCalled = false;
            var resetCalled = false;

            scriptHostManager
                .When(m => m.RestartHost())
                .Do(_ => restartCalled = true);

            scriptSettingManager
                .When(m => m.Reset())
                .Do(_ => resetCalled = true);

            ScriptSettingsManager.Instance = scriptSettingManager;
            var instanceManager = new InstanceManager(scriptHostManager, null, loggerFactory);

            var envValue = new
            {
                Name = Path.GetTempFileName().Replace(".", string.Empty),
                Value = Guid.NewGuid().ToString()
            };

            instanceManager.TryAssign(new HostAssignmentContext
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
