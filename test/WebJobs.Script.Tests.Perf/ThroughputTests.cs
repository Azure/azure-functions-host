// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Management.Compute;
using Microsoft.Azure.Management.Compute.Models;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;
using System;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebJobs.Script.Tests.EndToEnd.Shared;
using Xunit;


namespace WebJobs.Script.Tests.Perf
{
    [Collection(Constants.FunctionAppCollectionName)]
    public class ThroughputTests
    {
        private readonly FunctionAppFixture _fixture;

        public ThroughputTests(FunctionAppFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        [TestTrace]
        public async Task TestCSharp()
        {
            await ChangeLanguage("dotnet");

            await ExecutePS("win-csharp-ping.jmx", "C# Ping");
        }

        [Fact]
        [TestTrace]
        public async Task TestJS()
        {
            await ChangeLanguage("node");

            await ExecutePS("win-js-ping.jmx", "JS Ping");
        }

        [Fact]
        [TestTrace]
        public async Task TestJava()
        {
            // Do not forget to execute "Artifacts\PS\build-jar.ps1" to add jar file before running this test
            await ChangeLanguage("java");

            await ExecutePS("win-java-ping.jmx", "Java Ping");
        }

        private async Task ExecutePS(string scriptName, string description)
        {
            var authenticationContext = new AuthenticationContext($"https://login.windows.net/{Settings.SiteTenantId}");
            var credential = new ClientCredential(Settings.SiteApplicationId, Settings.SiteClientSecret);
            var result = authenticationContext.AcquireTokenAsync("https://management.core.windows.net/", credential);

            result.Wait();
            if (result.Result == null)
                throw new AuthenticationException("Failed to obtain the JWT token");

            var credentials = new TokenCredentials(result.Result.AccessToken);
            using (var client = new ComputeManagementClient(credentials))
            {
                client.SubscriptionId = Settings.SiteSubscriptionId;
                var commandResult = await VirtualMachinesOperationsExtensions.RunCommandAsync(client.VirtualMachines, Settings.SiteResourceGroup, Settings.VM,
                    new RunCommandInput("RunPowerShellScript",
                    new List<string>() { $"& 'C:\\Tools\\ps\\test-throughput.ps1' '{scriptName}' '{description}' '{Settings.RuntimeVersion}'" }));
            }
        }

        private async Task ChangeLanguage(string language)
        {
            await _fixture.AddAppSetting("FUNCTIONS_WORKER_RUNTIME", language);

            // Wait until the app fully restaeted and ready
            Thread.Sleep(30000);
            await _fixture.KuduClient.GetFunctions();
        }
    }
}
