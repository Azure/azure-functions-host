// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
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
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder()
                  .AddInMemoryCollection(new Dictionary<string, string>
                  {
                      ["languageWorker"] = "test",
                      ["languageWorkers:java:arguments"] = "-agentlib:jdwp=transport=dt_socket,server=y,suspend=n,address=5005"
                  });
            var config = configBuilder.Build();
            LanguageWorkerConfigurationService workerConfigurationService = new LanguageWorkerConfigurationService(config, loggerFactory);
            var configs = workerConfigurationService.WorkerConfigs;
            // Update config
            var updatedConfigBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder()
                  .AddInMemoryCollection(new Dictionary<string, string>
                  {
                      ["languageWorkers:java:arguments"] = "-agentlib:jdwp=transport=dt_socket,server=y,suspend=n,address=5006"
                  });
            var updatedConfig = updatedConfigBuilder.Build();
            workerConfigurationService.Reload(updatedConfig);
            configs = workerConfigurationService.WorkerConfigs;
        }
    }
}
