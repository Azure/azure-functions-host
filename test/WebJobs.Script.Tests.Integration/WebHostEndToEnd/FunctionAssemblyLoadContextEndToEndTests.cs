using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.WebJobs.Script.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.SamplesEndToEnd)]
    public class FunctionAssemblyLoadContextEndToEndTests : IDisposable
    {
        private readonly ITestOutputHelper _output;

        private bool _success = true;
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
                _launcher = CreateExtensionHost("AssemblyLoadContextRace");
                await _launcher.StartHostAsync(_output);

                var client = _launcher.HttpClient;
                var response = await client.GetAsync($"api/Function1");

                // The function does all the validation internally.
                Assert.True(HttpStatusCode.OK == response.StatusCode, $"Test failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
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
                Assert.False(string.IsNullOrWhiteSpace(connStr), "Configure the CosmosDB test connection before running the native dependency regression.");
                string cosmosKey = "ConnectionStrings__CosmosDB";

                var envVars = new Dictionary<string, string>
                {
                    { cosmosKey, connStr }
                };

                _launcher = CreateExtensionHost("NativeDependencyNoRuntimes", envVars);
                Assert.False(Directory.Exists(Path.Combine(_launcher.ScriptRootPath, "bin", "runtimes")),
                    "The native dependency fixture must exercise bin-directory fallback, not runtimes probing.");
                await _launcher.StartHostAsync();

                var client = _launcher.HttpClient;
                var response = await client.GetAsync($"api/NativeDependencyNoRuntimes");

                // The function does all the validation internally.
                Assert.True(HttpStatusCode.OK == response.StatusCode, $"Test failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            });
        }

        [Fact]
        public async Task MultipleDepenendencyVersions()
        {
            // Test that we consult the deps file 

            await RunTest(async () =>
            {
                _launcher = CreateExtensionHost("MultipleDependencyVersions");
                await _launcher.StartHostAsync();

                var client = _launcher.HttpClient;
                var response = await client.GetAsync($"api/MultipleDependencyVersions");

                // The function does all the validation internally.
                Assert.True(HttpStatusCode.OK == response.StatusCode, $"Test failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            });
        }

        [Fact]
        public async Task ReferenceOlderRuntimeAssembly()
        {
            // Test that we still return host version, even if it's a major version below.
            // The test project used repros the scenario because it references the Storage extension, 
            // which has references to Extensions.Hosting.Abstractions 2.1. The project itself directly
            // references 2.2 of this assembly and before the fix, would throw an exception on Startup.

            await RunTest(async () =>
            {
                _launcher = CreateExtensionHost("ReferenceOlderRuntimeAssembly");
                await _launcher.StartHostAsync();

                var client = _launcher.HttpClient;
                var response = await client.GetAsync($"api/ReferenceOlderRuntimeAssembly");

                // The function does all the validation internally.
                Assert.True(HttpStatusCode.OK == response.StatusCode, $"Test failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            });
        }

        private static HostProcessLauncher CreateExtensionHost(string projectName, IDictionary<string, string> environment = null)
        {
            environment ??= new Dictionary<string, string>();
            environment[EnvironmentSettingNames.FunctionWorkerRuntime] = RpcWorkerConstants.DotNetIsolatedLanguageWorkerName;

            return new HostProcessLauncher(projectName, environment);
        }

        private async Task RunTest(Func<Task> test)
        {
            try
            {
                await test();
            }
            catch (Exception)
            {
                _success = false;
                throw;
            }
        }

        public void Dispose()
        {
            _launcher?.Dispose();

            if (!_success && _launcher is not null)
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