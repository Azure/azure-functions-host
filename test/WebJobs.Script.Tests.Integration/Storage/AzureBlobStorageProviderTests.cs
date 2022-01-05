// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Storage
{
    public class AzureBlobStorageProviderTests
    {
        private const string StorageConnection = "AzureWebJobsStorage";

        [Fact]
        public async Task TestAzureBlobStorageProvider_ConnectionInWebHostConfiguration()
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

            var jobHostConfiguration = new ConfigurationBuilder().Build();

            var azureBlobStorageProvider = TestHelpers.GetAzureBlobStorageProvider(webHostConfiguration, jobHostConfiguration);
            azureBlobStorageProvider.TryCreateHostingBlobContainerClient(out var container);
            await VerifyContainerClientAvailable(container);

            Assert.True(azureBlobStorageProvider.TryCreateBlobServiceClientFromConnection(ConnectionStringNames.Storage, out BlobServiceClient blobServiceClient));
            await VerifyBlobServiceClientAvailable(blobServiceClient);
        }

        [Fact]
        public async Task TestAzureBlobStorageProvider_ConnectionInJobHostConfiguration()
        {
            var testConfiguration = TestHelpers.GetTestConfiguration();
            var testData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ConnectionStrings:AzureWebJobsStorage", testConfiguration.GetWebJobsConnectionString(StorageConnection) },
                { "AzureWebJobsStorage", "" }
            };

            var webHostConfiguration = new ConfigurationBuilder().Build();
            var jobHostConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(testData)
                .Build();

            var azureBlobStorageProvider = TestHelpers.GetAzureBlobStorageProvider(webHostConfiguration, jobHostConfiguration);
            azureBlobStorageProvider.TryCreateHostingBlobContainerClient(out var container);
            await VerifyContainerClientAvailable(container);

            Assert.True(azureBlobStorageProvider.TryCreateBlobServiceClientFromConnection(ConnectionStringNames.Storage, out BlobServiceClient blobServiceClient));
            await VerifyBlobServiceClientAvailable(blobServiceClient);
        }

        [Fact]
        public void TestAzureBlobStorageProvider_NoConnectionThrowsException()
        {
            var webHostConfiguration = new ConfigurationBuilder()
                .Build();
            var jobHostConfiguration = new ConfigurationBuilder()
                .Build();

            var azureBlobStorageProvider = TestHelpers.GetAzureBlobStorageProvider(webHostConfiguration, jobHostConfiguration);
            Assert.False(azureBlobStorageProvider.TryCreateHostingBlobContainerClient(out _));

            Assert.False(azureBlobStorageProvider.TryCreateBlobServiceClientFromConnection(ConnectionStringNames.Storage, out BlobServiceClient blobServiceClient));
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

            var azureBlobStorageProvider = TestHelpers.GetAzureBlobStorageProvider(webHostConfiguration, jobHostConfiguration);
            Assert.True(azureBlobStorageProvider.TryCreateBlobServiceClientFromConnection("Storage1", out BlobServiceClient client));
            Assert.Equal("webHostAccount", client.AccountName, ignoreCase: true);
        }

        private async Task VerifyBlobServiceClientAvailable(BlobServiceClient client)
        {
            try
            {
                var propertiesResponse = await client.GetPropertiesAsync();
                Assert.True(true);
            }
            catch (Exception e)
            {
                Assert.False(true, $"Could not establish connection to BlobService. {e}");
            }
        }

        private async Task VerifyContainerClientAvailable(BlobContainerClient client)
        {
            try
            {
                var propertiesResponse = await client.GetPropertiesAsync();
                Assert.True(true);
            }
            catch (Exception e)
            {
                Assert.False(true, $"Could not establish connection to BlobService. {e}");
            }
        }
    }
}
