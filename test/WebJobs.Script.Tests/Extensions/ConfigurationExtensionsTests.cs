// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Text;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Extensions
{
    public class ConfigurationExtensionsTests
    {
        [Fact]
        public void ConvertToJToken_ResultContainsAllNodes()
        {
            var hostJson = @"
{
  ""version"": ""2.0"",
  ""concurrency"": {
        ""dynamicConcurrencyEnabled"": true,
	    ""snapshotPersistenceEnabled"": true
  },
  ""extensions"": {
    ""durableTask"": {
      ""maxConcurrentActivityFunctions"": 10,
      ""maxConcurrentOrchestratorFunctions"": 12,
      ""storageProvider"": {
        ""type"": ""mssql"",
        ""connectionStringName"": ""SQLDB_Connection""
      }
    }
  },
  ""logging"": {
    ""applicationInsights"": {
        ""samplingSettings"": {
            ""isEnabled"": false
        },
      ""httpAutoCollectionOptions"": {
            ""enableW3CDistributedTracing"": true
      }
    }
  }
}
";

            var expectedExtensionsJson = @"{
  ""durableTask"": {
    ""maxConcurrentActivityFunctions"": ""10"",
    ""maxConcurrentOrchestratorFunctions"": ""12"",
    ""storageProvider"": {
      ""connectionStringName"": ""SQLDB_Connection"",
      ""type"": ""mssql""
    }
  }
}";

            var expectedConcurrencyJson = @"{
  ""dynamicConcurrencyEnabled"": ""true"",
  ""snapshotPersistenceEnabled"": ""true""
}";

            IConfiguration config = BuildConfigurationFromJson(hostJson);
            JToken extensions = config.Convert("extensions");
            JToken concurrency = config.Convert("concurrency");
            var extensionsJson = JsonConvert.SerializeObject(extensions, Formatting.Indented);
            var concurrencyJson = JsonConvert.SerializeObject(concurrency, Formatting.Indented);

            Assert.Equal(10, extensions.SelectToken("durableTask.maxConcurrentActivityFunctions"));
            Assert.Equal(12, extensions.SelectToken("durableTask.maxConcurrentOrchestratorFunctions"));
            Assert.Equal(true, concurrency["dynamicConcurrencyEnabled"]);
            Assert.Equal(true, concurrency["snapshotPersistenceEnabled"]);

            // An integer/boolean value will be transfered to a string value.
            // HostJosnFileConfigurationSource is also convert these into a string value.
            Assert.Equal(expectedExtensionsJson, extensionsJson);
            Assert.Equal(expectedConcurrencyJson, concurrencyJson);
        }

        [Fact]
        public void ConvertToJTokenWithEmptyJson_ResultIsPlainObject()
        {
            IConfiguration config = BuildConfigurationFromJson("{}");
            JToken extensions = config.Convert("extensions");
            Assert.NotNull(extensions);
            Assert.False(extensions.HasValues);
        }

        [Fact]
        public void ConvertToJTokenWithVersionOnly_ResultIsPlainObject()
        {
            IConfiguration config = BuildConfigurationFromJson("{\"version\":\"2.0\"}");
            JToken extensions = config.Convert("extensions");
            Assert.NotNull(extensions);
            Assert.False(extensions.HasValues);
        }

        [Fact]
        public void ConvertTOJTokenWithMissingSection_ResulthasObjectThatAvaiable()
        {
            // Concurrency secrtion is not avaiable but extensions section has it.
            var hostJson = @"
{
  ""version"": ""2.0"",
  ""extensions"": {
    ""durableTask"": {
      ""maxConcurrentActivityFunctions"": 10,
      ""maxConcurrentOrchestratorFunctions"": 12,
      ""storageProvider"": {
        ""type"": ""mssql"",
        ""connectionStringName"": ""SQLDB_Connection""
      }
    }
  }
}
";

            var expectedExtensionsJson = @"{
  ""durableTask"": {
    ""maxConcurrentActivityFunctions"": ""10"",
    ""maxConcurrentOrchestratorFunctions"": ""12"",
    ""storageProvider"": {
      ""connectionStringName"": ""SQLDB_Connection"",
      ""type"": ""mssql""
    }
  }
}";
            var expectedConcurrencyJson = @"{}";

            IConfiguration config = BuildConfigurationFromJson(hostJson);
            JToken extensions = config.Convert("extensions");
            JToken concurrency = config.Convert("concurrency");
            var extensionsJson = JsonConvert.SerializeObject(extensions, Formatting.Indented);
            var concurrencyJson = JsonConvert.SerializeObject(concurrency, Formatting.Indented);

            // Actual value is string type, however, it is converted to int.
            Assert.Equal(10, extensions.SelectToken("durableTask.maxConcurrentActivityFunctions"));
            Assert.Equal(12, extensions.SelectToken("durableTask.maxConcurrentOrchestratorFunctions"));

            Assert.Null(concurrency["dynamicConcurrencyEnabled"]);
            Assert.Null(concurrency["snapshotPersistenceEnabled"]);

            // An integer/boolean value will be changed into string see the expectedExtensionsJson, expectedConcurrencyJson.
            Assert.Equal(expectedExtensionsJson, extensionsJson);
            Assert.Equal(expectedConcurrencyJson, concurrencyJson);
        }

        private IConfiguration BuildConfigurationFromJson(string json)
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var builder = new ConfigurationBuilder()
                .AddJsonStream(stream);
            return builder.Build();
        }
    }
}
