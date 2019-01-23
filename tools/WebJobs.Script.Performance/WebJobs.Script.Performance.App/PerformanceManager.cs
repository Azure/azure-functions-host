using Microsoft.Azure.Management.Compute;
using Microsoft.Azure.Management.Compute.Models;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using WebJobs.Script.Tests.EndToEnd.Shared;

namespace WebJobs.Script.PerformanceMeter
{
    class PerformanceManager : IDisposable
    {
        private readonly FunctionAppFixture _fixture;
        private readonly ComputeManagementClient _client;
        private readonly List<TestDefinition> _tests;
        private bool _disposed = false;
        private string _currentExecutionRuntime;

        public PerformanceManager()
        {
            _fixture = new FunctionAppFixture();

            var authenticationContext = new AuthenticationContext($"https://login.windows.net/{Settings.SiteTenantId}");
            var credential = new ClientCredential(Settings.SiteApplicationId, Settings.SiteClientSecret);
            var result = authenticationContext.AcquireTokenAsync("https://management.core.windows.net/", credential);

            result.Wait();
            if (result.Result == null)
            {
                throw new AuthenticationException("Failed to obtain the JWT token");
            }

            var credentials = new TokenCredentials(result.Result.AccessToken);
            _client = new ComputeManagementClient(credentials);
            _client.SubscriptionId = Settings.SiteSubscriptionId;

            _tests = new List<TestDefinition>()
            {
                new TestDefinition()
                {
                    FileName = "win-csharp-ping.jmx",
                    Desciption = "C# Ping",
                    Runtime = "dotnet"
                },
                new TestDefinition()
                {
                    FileName = "win-js-ping.jmx",
                    Desciption = "JS Ping",
                    Runtime = "node"
                },
                new TestDefinition()
                {
                    FileName = "win-java-ping.jmx",
                    Desciption = "Java Ping",
                    Runtime = "java"
                }
            };
        }

        public async Task ExecuteAsync(string testId)
        {
            // We assume first word in testId is platform
            var test = _tests.FirstOrDefault(x => x.FileName.ToLower() == testId.ToLower());
            if (test == null)
            {
                Console.WriteLine($"Test '{testId}' is not found");
            }
            else
            {
                await ChangeExecutionRuntime(test.Runtime);
                _fixture.Logger.LogInformation($"Executing: {test.FileName}, {test.Desciption}");
                if (Environment.MachineName == "func-perf-vm")
                {
                    System.Diagnostics.Process process = new System.Diagnostics.Process();
                    System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
                    startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                    startInfo.FileName = "powershell.exe";
                    startInfo.Arguments = $"\"& 'C:\\Tools\\ps\\test-throughput.ps1'\" '{test.FileName}' '{test.Desciption}' '{Settings.RuntimeVersion}'";
                    Console.WriteLine($"Exectuing PS directly on VM: {startInfo.FileName}{startInfo.Arguments}");
                    process.StartInfo = startInfo;
                    process.Start();
                    process.WaitForExit();
                }
                else
                {
                    Console.WriteLine("Exectuing PS using VM operation API");
                    var commandResult = await VirtualMachinesOperationsExtensions.RunCommandAsync(_client.VirtualMachines, Settings.SiteResourceGroup, Settings.VM,
                        new RunCommandInput("RunPowerShellScript",
                        new List<string>() { $"& 'C:\\Tools\\ps\\test-throughput.ps1' '{test.FileName}' '{test.Desciption}' '{Settings.RuntimeVersion}'" }));
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _client.Dispose();
                _disposed = true;
            }
        }

        public async Task ExecuteAllAsync()
        {
            foreach(var test in _tests)
            {
                await ExecuteAsync(test.FileName);
            }
        }

        private async Task ChangeExecutionRuntime(string runtime)
        {
            if (_currentExecutionRuntime != runtime)
            {
                _fixture.Logger.LogInformation($"Changing execution runtime: {runtime}");
                await _fixture.AddAppSetting("FUNCTIONS_WORKER_RUNTIME", runtime);

                // Wait until the app fully restaeted and ready
                Thread.Sleep(30000);
                await _fixture.KuduClient.GetFunctions();
                _fixture.Logger.LogInformation($"Execution runtime is successfully changed: {runtime}");

                _currentExecutionRuntime = runtime;
            }
            else
            {
                _fixture.Logger.LogInformation($"Execution runtime: {runtime}");
            }
        }

        private class TestDefinition
        {
            public string FileName { get; set; }

            public string Runtime { get; set; }

            public string Desciption { get; set; }
        }
    }
}
