using System.Collections.Generic;
using System.Security.Authentication;
using System.Threading.Tasks;
using Microsoft.Azure.Management.Compute;
using Microsoft.Azure.Management.Compute.Models;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;

namespace WebJobs.Script.Tests.Perf.Dashboard
{
    public static class PerformanceManager
    {
        public static async Task Execute(string testId, PerformanceRunOptions options, ILogger log)
        {
            var authenticationContext = new AuthenticationContext($"https://login.windows.net/{options.TenantId}");
            var credential = new ClientCredential(options.ClientId, options.ClientSecret);
            var result = await authenticationContext.AcquireTokenAsync("https://management.core.windows.net/", credential);

            if (result == null)
            {
                throw new AuthenticationException("Failed to obtain the JWT token");
            }

            var credentials = new TokenCredentials(result.AccessToken);
            using (var client = new ComputeManagementClient(credentials))
            {
                client.SubscriptionId = options.SubscriptionId;
                await VirtualMachinesOperationsExtensions.BeginRunCommandAsync(client.VirtualMachines, options.SiteResourceGroup, options.VM,
                    new RunCommandInput("RunPowerShellScript",
                    new List<string>() { $"& 'C:\\Tools\\ps\\run.ps1' '{options.AppUrl}' '{testId}' '{options.ExtensionUrl}'" }));
            }
        }
    }
}
