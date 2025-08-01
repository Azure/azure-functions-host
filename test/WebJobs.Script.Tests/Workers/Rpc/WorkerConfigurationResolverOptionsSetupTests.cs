// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class WorkerConfigurationResolverOptionsSetupTests
    {
        [Fact]
        public void Configure_WithRealEnvironmentValues_SetsCorrectValues()
        {
            var testEnvironment = new TestEnvironment();
            var mockScriptHostManager = new Mock<IScriptHostManager>();
            var configBuilder = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}"] = "/default/workers",
                });
            var configuration = configBuilder.Build();
            var hostingOptions = new FunctionsHostingConfigOptions();

            var setup = new WorkerConfigurationResolverOptionsSetup(configuration, testEnvironment, mockScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            var options = new WorkerConfigurationResolverOptions();

            setup.Configure(options);

            Assert.Equal("/default/workers", options.WorkersDirPath);
        }
    }
}