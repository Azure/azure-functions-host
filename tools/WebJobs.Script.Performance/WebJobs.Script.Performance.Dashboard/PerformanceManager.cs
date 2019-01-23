using Microsoft.Azure.Management.Compute;
using Microsoft.Azure.Management.Compute.Models;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;
using System;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Threading.Tasks;

namespace WebJobs.Script.Tests.Perf.Dashboard
{
    public static class PerformanceManager
    {
        public static async Task Execute(string testIds, ILogger log)
        {
            string clientId = Environment.GetEnvironmentVariable("AzureWebJobsTargetSiteApplicationId", EnvironmentVariableTarget.Process);
            string clientSecret = Environment.GetEnvironmentVariable("AzureWebJobsTargetSiteClientSecret", EnvironmentVariableTarget.Process);
            string tenantId = Environment.GetEnvironmentVariable("AzureWebJobsTargetSiteTenantId", EnvironmentVariableTarget.Process);
            string subscriptionId = Environment.GetEnvironmentVariable("AzureWebJobsTargetSiteSubscriptionId", EnvironmentVariableTarget.Process);
            string siteResourceGroup = Environment.GetEnvironmentVariable("AzureWebJobsTargetSiteResourceGroup", EnvironmentVariableTarget.Process);
            string vm = Environment.GetEnvironmentVariable("AzureWebJobsVM", EnvironmentVariableTarget.Process);
            string functionsHostSlug = Environment.GetEnvironmentVariable("FunctionHostProjectSlug", EnvironmentVariableTarget.Process);
            string performanceMeterSlug = Environment.GetEnvironmentVariable("PerformanceProjectSlug", EnvironmentVariableTarget.Process);
            string extensionUrl = string.Empty;
            string appUrl = string.Empty;

            using (var appVeyorClient = new AppVeyorClient(log))
            {
                // Get latest private extension url from appvayor build
                string lastSuccessfulVersion = await appVeyorClient.GetLastSuccessfulBuildVersionAsync("dev", functionsHostSlug);
                extensionUrl = await appVeyorClient.GetArtifactUrlAsync(lastSuccessfulVersion, functionsHostSlug, "Image: Visual Studio 2017", "inproc");
                appUrl = await appVeyorClient.GetArtifactUrlAsync(lastSuccessfulVersion, functionsHostSlug, "Image: Visual Studio 2017", "WebJobs.Script.Performance.App");
            }

            var authenticationContext = new AuthenticationContext($"https://login.windows.net/{tenantId}");
            var credential = new ClientCredential(clientId, clientSecret);
            var result = await authenticationContext.AcquireTokenAsync("https://management.core.windows.net/", credential);

            if (result == null)
            {
                throw new AuthenticationException("Failed to obtain the JWT token");
            }

            var credentials = new TokenCredentials(result.AccessToken);
            using (var client = new ComputeManagementClient(credentials))
            {
                client.SubscriptionId = subscriptionId;
                string command = string.IsNullOrEmpty(testIds) ? string.Empty : $"-t {testIds}";
                command += string.IsNullOrEmpty(extensionUrl) ? string.Empty : $" -r {extensionUrl}";
                var commandResult = VirtualMachinesOperationsExtensions.RunCommand(client.VirtualMachines, siteResourceGroup, vm,
                    new RunCommandInput("RunPowerShellScript",
                    new List<string>() { $"& 'C:\\Tools\\ps\\run.ps1' '{appUrl}' '{command}'" }));
            }
        }
    }
}
