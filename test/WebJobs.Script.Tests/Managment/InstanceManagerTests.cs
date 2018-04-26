// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Managment
{
    public class InstanceManagerTests
    {
        [Fact]
        public async Task StartAssignment_AppliesAssignmentContext()
        {
            var loggerFactory = MockNullLogerFactory.CreateLoggerFactory();
            var settingsManager = new ScriptSettingsManager();
            var instanceManager = new InstanceManager(settingsManager, null, loggerFactory, null);
            var envValue = new
            {
                Name = Path.GetTempFileName().Replace(".", string.Empty),
                Value = Guid.NewGuid().ToString()
            };

            settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            WebScriptHostManager.ResetStandbyMode();
            var context = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>
                {
                    { envValue.Name, envValue.Value }
                }
            };
            bool result = instanceManager.StartAssignment(context);
            Assert.True(result);

            // specialization is done in the background
            await Task.Delay(500);

            var value = Environment.GetEnvironmentVariable(envValue.Name);
            Assert.Equal(value, envValue.Value);

            // calling again should return false, since we're no longer
            // in placeholder mode
            result = instanceManager.StartAssignment(context);
            Assert.False(result);
        }

        [Fact]
        public void StartAssignment_ReturnsFalse_WhenNotInStandbyMode()
        {
            var loggerFactory = MockNullLogerFactory.CreateLoggerFactory();
            var settingsManager = new ScriptSettingsManager();
            var instanceManager = new InstanceManager(settingsManager, null, loggerFactory, null);

            Assert.False(WebScriptHostManager.InStandbyMode);

            var context = new HostAssignmentContext();
            bool result = instanceManager.StartAssignment(context);
            Assert.False(result);
        }
    }
}
