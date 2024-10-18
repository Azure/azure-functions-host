// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class LanguageWorkerOptionsSetupTests
    {
        [Theory]
        [InlineData("DotNet")]
        [InlineData("dotnet")]
        [InlineData(null)]
        [InlineData("node")]
        public void LanguageWorkerOptions_Expected_ListOfConfigs(string workerRuntime)
        {
            var testEnvironment = new TestEnvironment();
            var testMetricLogger = new TestMetricsLogger();
            var configurationBuilder = new ConfigurationBuilder()
                .Add(new ScriptEnvironmentVariablesConfigurationSource());
            var configuration = configurationBuilder.Build();
            var testProfileManager = new Mock<IWorkerProfileManager>();
            var testScriptHostManager = new Mock<IScriptHostManager>();

            if (!string.IsNullOrEmpty(workerRuntime))
            {
                testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, workerRuntime);
            }
            else
            {
                // The dotnet-isolated worker only runs in placeholder mode. Setting the placeholder environment to 1 for the test.
                testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            }

            testProfileManager.Setup(pm => pm.LoadWorkerDescriptionFromProfiles(It.IsAny<RpcWorkerDescription>(), out It.Ref<RpcWorkerDescription>.IsAny))
                .Callback((RpcWorkerDescription defaultDescription, out RpcWorkerDescription outDescription) =>
                {
                    // dotnet-isolated worker config does not have "DefaultExecutablePath" in the parent level.So, we should set it from a profile.
                    if (defaultDescription.Language == "dotnet-isolated")
                    {
                        outDescription = new RpcWorkerDescription() { DefaultExecutablePath = "testPath", Language = "dotnet-isolated" };
                    }
                    else
                    {
                        // for other workers, we should return the default description as they have the "DefaultExecutablePath" in the parent level.
                        outDescription = defaultDescription;
                    }
                });

            LanguageWorkerOptionsSetup setup = new LanguageWorkerOptionsSetup(configuration, NullLoggerFactory.Instance, testEnvironment, testMetricLogger, testProfileManager.Object, testScriptHostManager.Object);
            LanguageWorkerOptions options = new LanguageWorkerOptions();

            setup.Configure(options);

            if (string.IsNullOrEmpty(workerRuntime))
            {
                Assert.Equal(5, options.WorkerConfigs.Count);
            }
            else if (workerRuntime.Equals(RpcWorkerConstants.DotNetLanguageWorkerName, StringComparison.OrdinalIgnoreCase))
            {
                Assert.Empty(options.WorkerConfigs);
            }
            else
            {
                Assert.Equal(1, options.WorkerConfigs.Count);
            }
        }
    }
}
