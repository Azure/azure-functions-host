// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd
{
    public class HostConfigurationExceptionTests : IDisposable
    {
        private readonly string _hostPath;

        public HostConfigurationExceptionTests()
        {
            _hostPath = Path.Combine(Path.GetTempPath(), "Functions", "HostConfigurationExceptionTests");
            Directory.CreateDirectory(_hostPath);
        }

        [Fact]
        public async Task HostStatusReturns_IfHostJsonError()
        {
            string hostJsonPath = Path.Combine(_hostPath, ScriptConstants.HostMetadataFileName);

            // Simulate a non-empty file without a 'version'
            JObject hostConfig = JObject.FromObject(new
            {
                functionTimeout = TimeSpan.FromSeconds(30)
            });

            await File.WriteAllTextAsync(hostJsonPath, hostConfig.ToString());

            var host = new TestFunctionHost(_hostPath, _ => { });

            // Ping the status endpoint to ensure we see the exception
            HostStatus status = await host.GetHostStatusAsync();
            Assert.Equal("Error", status.State);
            Assert.Equal("Microsoft.Azure.WebJobs.Script: The host.json file is missing the required 'version' property. See https://aka.ms/functions-hostjson for steps to migrate the configuration file.", status.Errors.Single());

            // Now update the file and make sure it auto-restarts.
            hostConfig["version"] = "2.0";
            await File.WriteAllTextAsync(hostJsonPath, hostConfig.ToString());

            await TestHelpers.Await(async () =>
            {
                status = await host.GetHostStatusAsync();
                return status.State == $"{ScriptHostState.Running}";
            });

            Assert.Null(status.Errors);
        }

        public void Dispose()
        {
            Directory.Delete(_hostPath, true);
        }
    }
}
