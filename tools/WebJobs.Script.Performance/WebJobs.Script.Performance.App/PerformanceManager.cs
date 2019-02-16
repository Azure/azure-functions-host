using Microsoft.Azure.Management.Compute;
using Microsoft.Azure.Management.Compute.Models;
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
using Microsoft.Extensions.Configuration;

namespace WebJobs.Script.PerformanceMeter
{
    class PerformanceManager : IDisposable
    {
        private readonly FunctionAppFixture _fixture;
        private readonly ComputeManagementClient _client;
        private readonly List<TestDefinition> _tests;
        private bool _disposed = false;

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

            _tests = new List<TestDefinition>();
            Settings.Tests.Bind(_tests);
        }

        public async Task ExecuteAsync(string testId)
        {
            // We assume first word in testId is platform
            var test = _tests.FirstOrDefault(x => x.Jmx.Contains(testId));
            if (test == null)
            {
                Console.WriteLine($"Test '{testId}' is not found");
            }
            else
            {
                await SetAppSettings(test);
                _fixture.Logger.LogInformation($"Executing: {test.Jmx}, {test.Desciption}");
                if (Environment.MachineName == "func-perf-vm")
                {
                    System.Diagnostics.Process process = new System.Diagnostics.Process();
                    System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
                    startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                    startInfo.FileName = "powershell.exe";
                    startInfo.Arguments = $"\"& 'C:\\Tools\\ps\\test-throughput.ps1'\" '{test.Jmx}' '{test.Desciption}' '{Settings.RuntimeVersion}'";
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
                        new List<string>() { $"& 'C:\\Tools\\ps\\test-throughput.ps1' '{test.Jmx}' '{test.Desciption}' '{Settings.RuntimeVersion}'" }));
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
                await ExecuteAsync(test.Jmx);
            }
        }

        private async Task SetAppSettings(TestDefinition test)
        {
            // Setting FUNCTIONS_WORKER_RUNTIME
            _fixture.Logger.LogInformation($"Changing FUNCTIONS_WORKER_RUNTIME: {test.Runtime}");
            await _fixture.AddAppSetting("FUNCTIONS_WORKER_RUNTIME", test.Runtime);

            // Setting
            _fixture.Logger.LogInformation($"Changing WEBSITE_RUN_FROM_PACKAGE: {test.RunFromZipUri}");
            await _fixture.AddAppSetting("WEBSITE_RUN_FROM_PACKAGE", test.RunFromZipUri);

            // Wait until the app fully restaeted and ready to run
            await _fixture.StopSite();
            await _fixture.StartSite();
            await _fixture.WaitForSite();
        }

        private class TestDefinition
        {
            public string Jmx { get; set; }

            public string Runtime { get; set; }

            public string Desciption { get; set; }

            public string RunFromZipUri { get; set; }
        }
    }
}
