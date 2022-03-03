// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Managment
{
    public class FunctionsSyncManagerTests
    {
        private const string HostJsonWithExtensions = @"
{
  ""version"": ""2.0"",
  ""logging"": {
    ""applicationInsights"": {
      ""samplingSettings"": {
        ""isEnabled"": true,
        ""excludedTypes"": ""Request""
      }
    }
  },
  ""extensionBundle"": {
    ""id"": ""Microsoft.Azure.Functions.ExtensionBundle"",
    ""version"": ""[2.*, 3.0.0)""
  },
  ""extensions"": {
    ""queues"": {
      ""maxPollingInterval"": ""00:00:30"",
      ""visibilityTimeout"": ""00:01:00"",
      ""batchSize"": 16,
      ""maxDequeueCount"": 5,
      ""newBatchThreshold"": 8,
      ""messageEncoding"": ""base64""
    }
  }
}
";

        private const string HostJsonWithoutExtensions = @"{}";

        private const string HostJsonWithWrongExtensions = @"
{
  ""version"": ""2.0"",
  ""extensions"": {
    ""queues"": 
      ""maxPollingInterval"": ""00:00:30"",
      ""visibilityTimeout"": ""00:01:00"",
      ""batchSize"": 16,
      ""maxDequeueCount"": 5,
      ""newBatchThreshold"": 8,
      ""messageEncoding"": ""base64""
    }
  }
}
";

        [Theory]
        [InlineData(HostJsonWithExtensions, false, false, "00:00:30")]
        [InlineData(HostJsonWithoutExtensions, true, false, "")]
        [InlineData(HostJsonWithExtensions, true, true, "")]
        [InlineData(HostJsonWithWrongExtensions, true, false, "")]
        public async Task GetHostJsonExtensionsKubernetesAsyncTest(string hostJsonContents, bool isNull, bool skipWriteFile, string value)
        {
            string scriptPath = string.Empty;
            try
            {
                var context = await SetupTestContextAsync(hostJsonContents, skipWriteFile);
                var json = await FunctionsSyncManager.GetHostJsonExtensionsForKubernetesAsync(context.Options, context.Logger);
                if (isNull)
                {
                    Assert.Null(json);
                }
                else
                {
                    Assert.Equal(value, json["queues"]?["maxPollingInterval"]);
                }
            }
            finally
            {
                await FileUtility.DeleteDirectoryAsync(scriptPath, true);
            }
        }

        [Theory]
        [InlineData("MultipleExtensionsWithDurablePayload.json", false, false, "UpdatedTaskHubName", "SQLDB_Connection", 12, 10)]
        [InlineData("MultipleExtensionsWithDurablePayload.json", true, true, null, null, 0, 0)] // host.json is not written
        [InlineData("NoExtensionSection.json", true, false, null, null, 0, 0)]
        [InlineData("NonDurableExtensionPayload.json", true, false, null, null, 0, 0)]
        [InlineData("DurableWithoutStorageProviderPayload.json", false, false, "UpdatedTaskHubName", null, 12, 0)]
        [InlineData("DurableWithStorageProviderPayload.json", false, false, null, "SQLDB_Connection", 0, 10)]
        public async Task GetHostJsonExtensionsAsyncTest(string hostJsonPayloadFile, bool isNull, bool skipWriteFile, string hubName, string connectionStringName, int maxConcurrentActivityFunctions, int maxConcurrentOrchestratorFunctions)
        {
            string scriptPath = string.Empty;
            var hostJsonContents = GetHostJsonFromFile(hostJsonPayloadFile);
            try
            {
                var context = await SetupTestContextAsync(hostJsonContents, skipWriteFile);
                var json = await FunctionsSyncManager.GetHostJsonExtensionsForDurableAsync(context.Options, context.Logger);
                if (isNull)
                {
                    Assert.Null(json);
                }
                else
                {
                    Assert.Equal(hubName, json.SelectToken("durableTask.hubName")?.ToString());
                    Assert.Equal(connectionStringName, json.SelectToken("durableTask.storageProvider.connectionStringName")?.ToString());
                    Assert.Equal(maxConcurrentActivityFunctions, int.Parse(json.SelectToken("durableTask.maxConcurrentActivityFunctions")?.ToString() ?? "0"));
                    Assert.Equal(maxConcurrentOrchestratorFunctions, int.Parse(json.SelectToken("durableTask.maxConcurrentOrchestratorFunctions")?.ToString() ?? "0"));
                }
            }
            finally
            {
                await FileUtility.DeleteDirectoryAsync(scriptPath, true);
            }
        }

        private async Task<TestContext> SetupTestContextAsync(string hostJsonContents, bool skipWriteFile)
        {
            var scriptPath = string.Empty;
            var logger = new Mock<ILogger>();
            var monitor = new Mock<IOptionsMonitor<ScriptApplicationHostOptions>>();
            var options = new ScriptApplicationHostOptions();
            scriptPath = GetTempDirectory();
            var hostJsonPath = Path.Combine(scriptPath, "host.json");
            if (!skipWriteFile)
            {
                await FileUtility.WriteAsync(hostJsonPath, hostJsonContents);
            }

            options.ScriptPath = scriptPath;
            monitor.Setup(x => x.CurrentValue).Returns(options);
            return new TestContext
            {
                Logger = logger.Object,
                Options = monitor.Object,
                ScriptPath = scriptPath
            };
        }

        private string GetTempDirectory()
        {
            var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(temp);
            return temp;
        }

        private string GetHostJsonFromFile(string fileName)
        {
            return File.ReadAllText(Path.Combine("Managment", "Payloads", fileName));
        }

        private class TestContext
        {
            public ILogger Logger { get; set; }

            public IOptionsMonitor<ScriptApplicationHostOptions> Options { get; set; }

            public string ScriptPath { get; set; }
        }
    }
}
