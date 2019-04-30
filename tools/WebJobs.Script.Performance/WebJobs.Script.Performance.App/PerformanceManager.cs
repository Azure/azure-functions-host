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
        }

        public async Task ExecuteAsync(Options options)
        {
            await SetAppSettings(options);
            _fixture.Logger.LogInformation($"Executing: {options.Jmx}, {options.Description}");
            if (Environment.MachineName == "func-perf-vm")
            {
                System.Diagnostics.Process process = new System.Diagnostics.Process();
                System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
                startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                startInfo.FileName = "powershell.exe";
                startInfo.Arguments = $"\"& 'C:\\Tools\\ps\\test-throughput.ps1'\" '{options.Jmx}' '{options.Description}' '{Settings.RuntimeVersion}'";
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
                    new List<string>() { $"& 'C:\\Tools\\ps\\test-throughput.ps1' '{options.Jmx}' '{options.Description}' '{Settings.RuntimeVersion}'" }));
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

        private async Task SetAppSettings(Options options)
        {
            // Setting FUNCTIONS_WORKER_RUNTIME
            _fixture.Logger.LogInformation($"Changing FUNCTIONS_WORKER_RUNTIME: {options.Runtime}");
            await _fixture.AddAppSetting("FUNCTIONS_WORKER_RUNTIME", options.Runtime);

            // Setting
            _fixture.Logger.LogInformation($"Changing WEBSITE_RUN_FROM_PACKAGE: {options.RunFromZip}");
            await _fixture.AddAppSetting("WEBSITE_RUN_FROM_PACKAGE", options.RunFromZip);

            // Wait until the app fully restaeted and ready to run
            await _fixture.StopSite();
            await _fixture.StartSite();
            await _fixture.WaitForSite();
        }
    }
}
