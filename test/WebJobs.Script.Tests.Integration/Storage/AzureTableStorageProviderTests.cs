// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Storage
{
    public class AzureTableStorageProviderTests
    {
        private const string StorageConnection = "AzureWebJobsStorage";

        [Fact]
        public async Task TryCreateHostingTableServiceClient_ConnectionInWebHostConfiguration()
        {
            var testConfiguration = TestHelpers.GetTestConfiguration();
            var testData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ConnectionStrings:AzureWebJobsStorage", testConfiguration.GetWebJobsConnectionString(StorageConnection) },
                { "AzureWebJobsStorage", "" }
            };

            var webHostConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(testData)
                .Build();

            var azureTableStorageProvider = TestHelpers.GetAzureTableStorageProvider(webHostConfiguration);
            azureTableStorageProvider.TryCreateHostingTableServiceClient(out var client);
            await VerifyTableServiceClientAvailable(client);
        }

        [Fact]
        public async Task TryCreateHostingTableServiceClient_ConnectionInJobHostConfiguration()
        {
            var testConfiguration = TestHelpers.GetTestConfiguration();
            var testData = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "ConnectionStrings:AzureWebJobsStorage", testConfiguration.GetWebJobsConnectionString(StorageConnection) },
                { "AzureWebJobsStorage", "" }
            };

            var webHostConfiguration = new ConfigurationBuilder().Build();
            var jobHostConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(testData)
                .Build();

            var azureTableStorageProvider = TestHelpers.GetAzureTableStorageProvider(webHostConfiguration, jobHostConfiguration);
            azureTableStorageProvider.TryCreateHostingTableServiceClient(out TableServiceClient client);
            await VerifyTableServiceClientAvailable(client);
        }

        [Theory]
        [InlineData("AzureWebJobsStorage:accountName", "storage")]
        [InlineData("AzureWebJobsStorage:AccountName", "storage")]
        [InlineData("AzureWebJobsStorage:tableServiceUri", "https://mytable.functions")]
        [InlineData("AzureWebJobsStorage:TableServiceUri", "https://mytable.functions")]
        public void TryCreateHostingTableServiceClient_IdentityBasedConnections(string key, string value)
        {
            var testData = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { key, value }
            };

            // Using a case sensitive data source to match the ScriptEnvironmentVariablesConfigurationSource behavior on Linux (case-sensitive env vars)
            var webHostConfiguration = new ConfigurationBuilder().Build();
            var jobHostConfiguration = new ConfigurationBuilder()
                .Add(new CaseSensitiveConfigurationSource(testData))
                .Build();

            var azureTableStorageProvider = TestHelpers.GetAzureTableStorageProvider(webHostConfiguration, jobHostConfiguration);
            Assert.True(azureTableStorageProvider.TryCreateHostingTableServiceClient(out _));
        }

        [Fact]
        public void TryCreateHostingTableServiceClient_NoConnectionThrowsException()
        {
            var webHostConfiguration = new ConfigurationBuilder()
                .Build();

            var azureTableStorageProvider = TestHelpers.GetAzureTableStorageProvider(webHostConfiguration);
            Assert.False(azureTableStorageProvider.TryCreateHostingTableServiceClient(out _));

            Assert.False(azureTableStorageProvider.TryCreateTableServiceClient(ConnectionStringNames.Storage, out TableServiceClient blobServiceClient));
        }

        [Theory]
        [InlineData("ConnectionStrings:AzureWebJobsStorage1")]
        [InlineData("AzureWebJobsStorage1")]
        [InlineData("Storage1")]
        public void TestAzureBlobStorageProvider_JobHostConfigurationWinsConflict(string connectionName)
        {
            var bytes = Encoding.UTF8.GetBytes("someKey");
            var encodedString = Convert.ToBase64String(bytes);

            var webHostConfigData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { connectionName, $"DefaultEndpointsProtocol=https;AccountName=webHostAccount;AccountKey={encodedString};EndpointSuffix=core.windows.net" },
            };
            var webHostConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(webHostConfigData)
                .Build();

            var jobHostConfigData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { connectionName, $"DefaultEndpointsProtocol=https;AccountName=jobHostAccount;AccountKey={encodedString};EndpointSuffix=core.windows.net" },
            };
            var jobHostConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(jobHostConfigData)
                .Build();

            var azureTableStorageProvider = TestHelpers.GetAzureTableStorageProvider(webHostConfiguration, jobHostConfiguration);
            Assert.True(azureTableStorageProvider.TryCreateTableServiceClient("Storage1", out TableServiceClient client));
            Assert.Equal("webHostAccount", client.AccountName, ignoreCase: true);
        }

        private async Task VerifyTableServiceClientAvailable(TableServiceClient client)
        {
            try
            {
                var propertiesResponse = await client.GetPropertiesAsync();
                Assert.True(true);
            }
            catch (Exception e)
            {
                Assert.False(true, $"Could not establish connection to TableService. {e}");
            }
        }

        private class CaseSensitiveConfigurationSource : IConfigurationSource
        {
            private readonly Dictionary<string, string> _data;
            public CaseSensitiveConfigurationSource(Dictionary<string, string> data)
            {
                _data = data;
            }
            public IConfigurationProvider Build(IConfigurationBuilder builder)
            {
                return new CaseSensitiveConfigurationProvider(_data);
            }
        }

        private class CaseSensitiveConfigurationProvider : ConfigurationProvider
        {
            private readonly Dictionary<string, string> _data;
            public CaseSensitiveConfigurationProvider(Dictionary<string, string> data)
            {
                _data = data;
            }
            public override void Load()
            {
                // _data might be already case-insensitive but we want to ensure it's case-sensitive
                Data = new Dictionary<string, string>(_data, StringComparer.Ordinal);
            }
        }
    }
}
