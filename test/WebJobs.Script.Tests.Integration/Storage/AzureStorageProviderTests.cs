﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Storage
{
    public class AzureStorageProviderTests
    {
        private const string StorageConnection = "AzureWebJobsStorage";

        [Fact]
        public async Task TestAzureStorageProvider_ConnectionInWebHostConfiguration()
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

            var azureStorageProvider = GetAzureStorageProvider(webHostConfiguration, jobHostConfiguration);
            azureStorageProvider.TryCreateHostingBlobContainerClient(out var container);
            await VerifyContainerClientAvailable(container);

            Assert.True(azureStorageProvider.TryCreateBlobServiceClientFromConnection(ConnectionStringNames.Storage, out BlobServiceClient blobServiceClient));
            await VerifyBlobServiceClientAvailable(blobServiceClient);
        }

        [Fact]
        public async Task TestAzureStorageProvider_ConnectionInJobHostConfiguration()
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

            var azureStorageProvider = GetAzureStorageProvider(webHostConfiguration, jobHostConfiguration);
            azureStorageProvider.TryCreateHostingBlobContainerClient(out var container);
            await VerifyContainerClientAvailable(container);

            Assert.True(azureStorageProvider.TryCreateBlobServiceClientFromConnection(ConnectionStringNames.Storage, out BlobServiceClient blobServiceClient));
            await VerifyBlobServiceClientAvailable(blobServiceClient);
        }

        [Fact]
        public void TestAzureStorageProvider_NoConnectionThrowsException()
        {
            var webHostConfiguration = new ConfigurationBuilder()
                .Build();
            var jobHostConfiguration = new ConfigurationBuilder()
                .Build();

            var azureStorageProvider = GetAzureStorageProvider(webHostConfiguration, jobHostConfiguration);
            Assert.False(azureStorageProvider.TryCreateHostingBlobContainerClient(out _));

            Assert.False(azureStorageProvider.TryCreateBlobServiceClientFromConnection(ConnectionStringNames.Storage, out BlobServiceClient blobServiceClient));
        }

        [Theory]
        [InlineData("ConnectionStrings:AzureWebJobsStorage1")]
        [InlineData("AzureWebJobsStorage1")]
        [InlineData("Storage1")]
        public void TestAzureStorageProvider_JobHostConfigurationWinsConflict(string connectionName)
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

            var azureStorageProvider = GetAzureStorageProvider(webHostConfiguration, jobHostConfiguration);
            Assert.True(azureStorageProvider.TryCreateBlobServiceClientFromConnection("Storage1", out BlobServiceClient client));
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

        private static IAzureBlobStorageProvider GetAzureStorageProvider(IConfiguration webHostConfiguration, IConfiguration jobHostConfiguration, JobHostInternalStorageOptions storageOptions = null)
        {
            IHost tempHost = new HostBuilder()
                .ConfigureServices(services =>
                {
                    // Override configuration
                    services.AddSingleton(webHostConfiguration);
                    services.AddAzureBlobStorageProvider();

                    var testServiceProvider = new TestScriptHostService(jobHostConfiguration);
                    services.AddSingleton<IScriptHostManager>(testServiceProvider);
                    if (storageOptions != null)
                    {
                        services.AddTransient<IOptions<JobHostInternalStorageOptions>>(s => new OptionsWrapper<JobHostInternalStorageOptions>(storageOptions));
                    }
                }).Build();

            var azureStorageProvider = tempHost.Services.GetRequiredService<IAzureBlobStorageProvider>();
            return azureStorageProvider;
        }

        private class TestScriptHostService : IScriptHostManager, IServiceProvider
        {
            private readonly IConfiguration _configuration;

            public TestScriptHostService(IConfiguration configuration)
            {
                _configuration = configuration;
            }
            ScriptHostState IScriptHostManager.State => throw new NotImplementedException();

            Exception IScriptHostManager.LastError => throw new NotImplementedException();

            event EventHandler IScriptHostManager.HostInitializing
            {
                add
                {
                    throw new NotImplementedException();
                }

                remove
                {
                    throw new NotImplementedException();
                }
            }

            public event EventHandler<ActiveHostChangedEventArgs> ActiveHostChanged;

            public void OnActiveHostChanged()
            {
                ActiveHostChanged?.Invoke(this, new ActiveHostChangedEventArgs(null, null));
            }

            object IServiceProvider.GetService(Type serviceType)
            {
                if (serviceType == typeof(IConfiguration))
                {
                    return _configuration;
                }

                throw new NotImplementedException();
            }

            Task IScriptHostManager.RestartHostAsync(CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }
        }
    }
}
