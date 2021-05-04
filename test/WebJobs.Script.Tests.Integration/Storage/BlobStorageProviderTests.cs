// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs.Script.StorageProvider;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Storage
{
    /// <summary>
    /// TODO: TEMP - implementation should be moved https://github.com/Azure/azure-webjobs-sdk/issues/2710
    /// Tests whether the StorageClientProvider can properly create a client and send a request
    /// </summary>
    public class BlobStorageProviderTests
    {
        private const string StorageConnection = "AzureWebJobsStorage";

        private readonly BlobServiceClientProvider _blobServiceClientProvider;
        private readonly IConfiguration _configuration; 

        public BlobStorageProviderTests()
        {
            _configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddTestSettings()
                .Build();

            _blobServiceClientProvider = GetBlobServiceClientProvider(_configuration);
        }

        [Fact]
        public async Task TestBlobStorageProvider_TryConnectionName()
        {
            Assert.True(_blobServiceClientProvider.TryGet(StorageConnection, out BlobServiceClient client));
            await VerifyServiceAvailable(client);
        }

        [Fact]
        public async Task TestBlobStorageProvider_ConnectionName()
        {
            BlobServiceClient client = _blobServiceClientProvider.Get(StorageConnection);
            await VerifyServiceAvailable(client);
        }

        [Fact]
        public async Task TestBlobStorageProvider_ConnectionNameWithResolver()
        {
            var resolver = new DefaultNameResolver(_configuration);

            BlobServiceClient client = _blobServiceClientProvider.Get(StorageConnection, resolver);
            await VerifyServiceAvailable(client);
        }

        [Fact]
        public async Task TestBlobStorageProvider_TryConnectionString()
        {
            var connectionString = _configuration[StorageConnection];

            Assert.True(_blobServiceClientProvider.TryGetFromConnectionString(connectionString, out BlobServiceClient client));
            await VerifyServiceAvailable(client);
        }

        private async Task VerifyServiceAvailable(BlobServiceClient client)
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

        private static BlobServiceClientProvider GetBlobServiceClientProvider(IConfiguration configuration, JobHostInternalStorageOptions storageOptions = null)
        {
            IHost tempHost = new HostBuilder()
                .ConfigureServices(services =>
                {
                    // Override configuration
                    services.AddSingleton(configuration);
                    services.AddAzureStorageBlobs();
                }).Build();

            var blobServiceClientProvider = tempHost.Services.GetRequiredService<BlobServiceClientProvider>();
            return blobServiceClientProvider;
        }
    }
}