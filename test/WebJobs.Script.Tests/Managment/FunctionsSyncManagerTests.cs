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
        public async Task GetHostJsonExtensionsAsyncTest(string hostJsonContents, bool isNull, bool skipWriteFile, string value)
        {
            string scriptPath = string.Empty;
            try
            {
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
                var json = await FunctionsSyncManager.GetHostJsonExtensionsAsync(monitor.Object, logger.Object);
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

        private string GetTempDirectory()
        {
            var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(temp);
            return temp;
        }
    }
}
