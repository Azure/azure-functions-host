// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Managment
{
    public class InstanceManagerTests : IDisposable
    {
        [Fact]
        public async Task StartAssignmentShouldUpdateEnvironmentVariables()
        {
            var loggerFactory = MockNullLogerFactory.CreateLoggerFactory();

            var instanceManager = new InstanceManager(null, loggerFactory, null);
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

            // specialization is done in the background
            await Task.Delay(500);

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
