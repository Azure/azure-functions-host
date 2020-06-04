// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd
{
    public class HostConfigurationExceptionTests : IDisposable
    {
        private readonly string _hostPath;
        private TestFunctionHost _host;

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

            string logPath = Path.Combine(Path.GetTempPath(), @"Functions");
            _host = new TemporaryTestFunctionHost(_hostPath, logPath, _ => { });

            // Ping the status endpoint to ensure we see the exception

            // TODO: the HostStatus.State should be Error. But there's a bug at the moment causing the status to be
            // initialized intermittently
            HostStatus status = await _host.GetHostStatusAsync();
            Assert.Equal("Microsoft.Azure.WebJobs.Script: The host.json file is missing the required 'version' property. See https://aka.ms/functions-hostjson for steps to migrate the configuration file.", status.Errors.Single());

            // Due to https://github.com/Azure/azure-functions-host/issues/1351, slow this down to ensure
            // we have a host running and watching for file changes.
            await TestHelpers.Await(() =>
            {
                return _host.GetLog().Contains("[Microsoft.Extensions.Hosting.Internal.Host] Hosting started");
            });

            // Now update the file and make sure it auto-restarts.
            hostConfig["version"] = "2.0";
            await File.WriteAllTextAsync(hostJsonPath, hostConfig.ToString());

            await TestHelpers.Await(async () =>
            {
                status = await _host.GetHostStatusAsync();
                return status.State == $"{ScriptHostState.Running}";
            }, userMessageCallback: _host.GetLog);

            Assert.Null(status.Errors);
        }

        public void Dispose()
        {
            try
            {
                _host.Dispose();
                Directory.Delete(_hostPath, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private class TemporaryTestFunctionHost : TestFunctionHost
        {
            public TemporaryTestFunctionHost(string scriptPath, string logPath,
                Action<IServiceCollection> configureWebHostServices = null,
                Action<IWebJobsBuilder> configureScriptHostWebJobsBuilder = null,
                Action<IConfigurationBuilder> configureScriptHostAppConfiguration = null,
                Action<ILoggingBuilder> configureScriptHostLogging = null,
                Action<IServiceCollection> configureScriptHostServices = null)
                : base(scriptPath, logPath, configureWebHostServices, configureScriptHostWebJobsBuilder, configureScriptHostAppConfiguration, configureScriptHostLogging, configureScriptHostServices)
            {
            }
        }
    }
}
