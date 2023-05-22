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

            if (!string.IsNullOrEmpty(workerRuntime))
            {
                testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, workerRuntime);
            }

            LanguageWorkerOptionsSetup setup = new LanguageWorkerOptionsSetup(configuration, NullLoggerFactory.Instance, testEnvironment, testMetricLogger, testProfileManager.Object);
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
