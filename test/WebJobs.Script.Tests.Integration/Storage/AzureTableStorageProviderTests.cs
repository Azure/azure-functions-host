// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
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
        public async Task TestAzureTableStorageProvider_ConnectionInWebHostConfiguration()
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
        public void TestAzureTableStorageProvider_NoConnectionThrowsException()
        {
            var webHostConfiguration = new ConfigurationBuilder()
                .Build();

            var azureTableStorageProvider = TestHelpers.GetAzureTableStorageProvider(webHostConfiguration);
            Assert.False(azureTableStorageProvider.TryCreateHostingTableServiceClient(out _));

            Assert.False(azureTableStorageProvider.TryCreateTableServiceClient(ConnectionStringNames.Storage, out TableServiceClient blobServiceClient));
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
    }
}
