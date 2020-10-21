﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Tests.EndToEnd;
using Microsoft.Extensions.Configuration;
using Microsoft.WebJobs.Script.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, nameof(CSharpEndToEndTests))]
    public class FunctionAssemblyLoadContextEndToEndTests : IDisposable
    {
        private readonly ITestOutputHelper _output;

        private bool success = true;
        private HostProcessLauncher _launcher;

        public FunctionAssemblyLoadContextEndToEndTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task Fallback_IsThreadSafe()
        {
            await RunTest(async () =>
            {
                _launcher = new HostProcessLauncher("AssemblyLoadContextRace");
                await _launcher.StartHostAsync();

                var client = _launcher.HttpClient;
                var response = await client.GetAsync($"api/Function1");

                // The function does all the validation internally.
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            });
        }

        [Fact]
        public async Task NativeDependency_Quirks()
        {
            // Test a specific bug that hit on v2 with earlier versions of the VS SDK, only 
            // when publishing

            await RunTest(async () =>
            {
                var config = TestHelpers.GetTestConfiguration();
                var connStr = config.GetConnectionString("CosmosDB");
                string cosmosKey = "ConnectionStrings__CosmosDB";

                var envVars = new Dictionary<string, string>
                {
                    { cosmosKey, connStr }
                };

                _launcher = new HostProcessLauncher("NativeDependencyOldSdk", envVars, usePublishPath: true, "netcoreapp2.2");
                await _launcher.StartHostAsync();

                var client = _launcher.HttpClient;
                var response = await client.GetAsync($"api/NativeDependencyOldSdk");

                // The function does all the validation internally.
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            });
        }

        [Fact]
        public async Task NativeDependency_NoRuntimes()
        {
            // Test that we load the correct native assembly when built against a rid, which removed
            // the runtimes folder. We should be finding the assembly in the bin dir directly.

            await RunTest(async () =>
            {
                var config = TestHelpers.GetTestConfiguration();
                var connStr = config.GetConnectionString("CosmosDB");
                string cosmosKey = "ConnectionStrings__CosmosDB";

                var envVars = new Dictionary<string, string>
                {
                    { cosmosKey, connStr }
                };

                _launcher = new HostProcessLauncher("NativeDependencyNoRuntimes", envVars);
                await _launcher.StartHostAsync();

                var client = _launcher.HttpClient;
                var response = await client.GetAsync($"api/NativeDependencyNoRuntimes");

                // The function does all the validation internally.
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            });
        }

        [Fact]
        public async Task MultipleDepenendencyVersions()
        {
            // Test that we consult the deps file 

            await RunTest(async () =>
            {
                _launcher = new HostProcessLauncher("MultipleDependencyVersions");
                await _launcher.StartHostAsync();

                var client = _launcher.HttpClient;
                var response = await client.GetAsync($"api/MultipleDependencyVersions");

                // The function does all the validation internally.
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            });
        }

        private async Task RunTest(Func<Task> test)
        {
            try
            {
                await test();
            }
            catch (Exception)
            {
                success = false;
                throw;
            }
        }


        public void Dispose()
        {
            _launcher?.Dispose();

            if (!success)
            {
                // Dump logs to console
                _output.WriteLine("=== Output ===");
                foreach (var log in _launcher.OutputLogs)
                {
                    _output.WriteLine(log);
                }

                _output.WriteLine("=== Errors ===");
                foreach (var log in _launcher.ErrorLogs)
                {
                    _output.WriteLine(log);
                }
            }
        }
    }
}
