// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ARMClient.Authentication.Contracts;
using Colors.Net;
using NSubstitute;
using WebJobs.Script.Cli.Arm;
using WebJobs.Script.Cli.Arm.Models;
using WebJobs.Script.Cli.Extensions;
using WebJobs.Script.Cli.Interfaces;
using WebJobs.Script.Cli.Verbs.List;
using Xunit;

namespace WebJobs.Script.Cli.Tests.VerbsTest
{
    public class ListVerbTests
    {
        [Theory]
        [InlineData("appName", "West US")]
        [InlineData(null, null)]
        public async Task ListFunctionAppsTest(string functionAppName, string location)
        {
            // Setup
            var armManager = Substitute.For<IArmManager>();
            var stdout = Substitute.For<IConsoleWriter>();
            var stderr = Substitute.For<IConsoleWriter>();
            var secretsManager = Substitute.For<ISecretsManager>();
            var tipsManager = Substitute.For<ITipsManager>();

            ColoredConsole.Out = stdout;
            ColoredConsole.Error = stderr;

            var apps = functionAppName != null
                ? new[] { new Site(string.Empty, string.Empty, functionAppName) { Location = location } }.AsEnumerable()
                : Enumerable.Empty<Site>();

            armManager.GetFunctionAppsAsync().Returns(apps);
            armManager.GetCurrentTenantAsync().Returns(new TenantCacheInfo());
            armManager.GetUserAsync().Returns(new ArmWebsitePublishingCredentials { PublishingUserName = "test" });

            // Test
            var listVerb = new ListFunctionApps(armManager, tipsManager);

            await listVerb.RunAsync();

            // Assert
            armManager
                .Received()
                .GetFunctionAppsAsync()
                .Ignore();

            if (apps.Any())
            {
                stdout
                    .Received()
                    .WriteLine(Arg.Is<object>(v => v.ToString().Contains(functionAppName)));
            }
            else
            {
                stderr
                    .Received()
                    .WriteLine(Arg.Is<object>(e => e.ToString().Contains("No function apps found")));
            }
        }

        [Theory]
        [InlineData("storageAccountName", "West US")]
        [InlineData(null, null)]
        public async Task ListStorageAccountsTest(string storageAccountName, string location)
        {
            // Setup
            var armManager = Substitute.For<IArmManager>();
            var stdout = Substitute.For<IConsoleWriter>();
            var stderr = Substitute.For<IConsoleWriter>();
            var secretsManager = Substitute.For<ISecretsManager>();
            var tipsManager = Substitute.For<ITipsManager>();

            ColoredConsole.Out = stdout;
            ColoredConsole.Error = stderr;

            var storageAccounts = storageAccountName != null
                ? new[] { new StorageAccount(string.Empty, string.Empty, storageAccountName, location) }.AsEnumerable()
                : Enumerable.Empty<StorageAccount>();

            armManager.GetStorageAccountsAsync().Returns(storageAccounts);
            armManager.GetCurrentTenantAsync().Returns(new TenantCacheInfo());
            armManager.GetUserAsync().Returns(new ArmWebsitePublishingCredentials { PublishingUserName = "test" });

            // Test
            var listVerb = new ListStorageAccounts(armManager, tipsManager);

            await listVerb.RunAsync();

            // Assert
            armManager
                .Received()
                .GetStorageAccountsAsync()
                .Ignore();

            if (storageAccounts.Any())
            {
                stdout
                    .Received()
                    .WriteLine(Arg.Is<object>(v => v.ToString().Contains(storageAccountName)));
            }
            else
            {
                stderr
                    .Received()
                    .WriteLine(Arg.Is<object>(e => e.ToString().Contains("No storage accounts found")));
            }
        }

        [Theory]
        [InlineData("secretName", "secretValue")]
        [InlineData(null, null)]
        [InlineData(null, null)]
        public async Task ListSecretsTest(string secretName, string secretValue)
        {
            // Setup
            var stdout = Substitute.For<IConsoleWriter>();
            var stderr = Substitute.For<IConsoleWriter>();
            var secretsManager = Substitute.For<ISecretsManager>();
            var tipsManager = Substitute.For<ITipsManager>();

            ColoredConsole.Out = stdout;
            ColoredConsole.Error = stderr;

            var secrets = secretName != null
                ? new Dictionary<string, string>() { { secretName, secretValue } }
                : new Dictionary<string, string>();

            secretsManager.GetSecrets().Returns(secrets);

            // Test
            var listVerb = new ListSecrets(secretsManager, tipsManager);

            await listVerb.RunAsync();

            // Assert
            secretsManager
                .Received()
                .GetSecrets();

            if (secrets.Any())
            {
                stdout
                    .Received()
                    .WriteLine(Arg.Is<object>(v => v.ToString().Contains(secretName)));
            }
            else if (!secrets.Any())
            {
                stdout
                    .Received()
                    .WriteLine(Arg.Is<object>(e => e.ToString().Contains("No secrets currently configured locally")));
            }
        }

        [Fact]
        public async Task ListTenantsTests()
        {
            // Setup
            var armManager = Substitute.For<IArmManager>();
            var tipsManager = Substitute.For<ITipsManager>();

            armManager.DumpTokenCache().Returns(Enumerable.Empty<string>());

            // Test
            var listVerb = new ListTenants(armManager, tipsManager);

            await listVerb.RunAsync();

            // Assert
            armManager
                .Received()
                .DumpTokenCache();
        }
    }
}
