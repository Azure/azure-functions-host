// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class LanguageWorkerConfigurationServiceTests
    {
        [Fact]
        public void LanguageWorker_WorkersDir_Set()
        {
            var loggerFactory = MockNullLoggerFactory.CreateLoggerFactory();
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder();
            var defaultConfig = configBuilder.Build();
            LanguageWorkerConfigurationService workerConfigurationService = new LanguageWorkerConfigurationService(defaultConfig, loggerFactory);
            var defaultWorkerConfigs = workerConfigurationService.WorkerConfigs;
            var defaultJavaConfig = defaultWorkerConfigs.Where(c => c.Language.Equals(LanguageWorkerConstants.JavaLanguageWorkerName)).FirstOrDefault();
            List<string> defaultJavaArguments = defaultJavaConfig.Arguments.ExecutableArguments;
            Assert.True(defaultJavaArguments.Count() == 1);
            Assert.False(defaultJavaArguments.ElementAtOrDefault(0).Contains("address=5006"));

            // Update config
            var updatedConfigBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder()
                  .AddInMemoryCollection(new Dictionary<string, string>
                  {
                      ["languageWorkers:java:arguments"] = "-agentlib:jdwp=transport=dt_socket,server=y,suspend=n,address=5006"
                  });
            var updatedConfig = updatedConfigBuilder.Build();
            workerConfigurationService.Reload(updatedConfig);

            var updatedWorkerConfigs = workerConfigurationService.WorkerConfigs;
            var updatedJavaConfig = updatedWorkerConfigs.Where(c => c.Language.Equals(LanguageWorkerConstants.JavaLanguageWorkerName)).FirstOrDefault();
            List<string> updatedJavaArguments = updatedJavaConfig.Arguments.ExecutableArguments;
            Assert.True(updatedJavaArguments.Count() == 2);
            Assert.True(updatedJavaArguments.ElementAtOrDefault(1).Contains("address=5006"));
        }
    }
}
