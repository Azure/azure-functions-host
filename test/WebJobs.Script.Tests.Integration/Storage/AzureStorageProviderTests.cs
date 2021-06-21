// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
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
            var testData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ConnectionStrings:AzureWebJobsStorage", Environment.GetEnvironmentVariable(StorageConnection) },
                { "AzureWebJobsStorage", "" }
            };

            using (new TestScopedEnvironmentVariable(testData))
            {
                var webHostConfiguration = new ConfigurationBuilder()
                    .AddEnvironmentVariables()
                    .AddTestSettings()
                    .Build();

                var jobHostConfiguration = new ConfigurationBuilder().Build();

                var azureStorageProvider = GetAzureStorageProvider(webHostConfiguration, jobHostConfiguration);
                var container = azureStorageProvider.GetBlobContainerClient();
                await VerifyContainerClientAvailable(container);

                Assert.True(azureStorageProvider.TryGetBlobServiceClientFromConnection(out BlobServiceClient blobServiceClient, ConnectionStringNames.Storage));
                await VerifyBlobServiceClientAvailable(blobServiceClient);
            }
        }

        [Fact]
        public async Task TestAzureStorageProvider_ConnectionInJobHostConfiguration()
        {
            var testData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ConnectionStrings:AzureWebJobsStorage", Environment.GetEnvironmentVariable(StorageConnection) },
                { "AzureWebJobsStorage", "" }
            };

            using (new TestScopedEnvironmentVariable(testData))
            {
                var webHostConfiguration = new ConfigurationBuilder().Build();

                var jobHostConfiguration = new ConfigurationBuilder()
                    .AddEnvironmentVariables()
                    .AddTestSettings()
                    .Build();

                var azureStorageProvider = GetAzureStorageProvider(webHostConfiguration, jobHostConfiguration);
                var container = azureStorageProvider.GetBlobContainerClient();
                await VerifyContainerClientAvailable(container);

                Assert.True(azureStorageProvider.TryGetBlobServiceClientFromConnection(out BlobServiceClient blobServiceClient, ConnectionStringNames.Storage));
                await VerifyBlobServiceClientAvailable(blobServiceClient);
            }
        }

        [Fact]
        public void TestAzureStorageProvider_NoConnectionThrowsException()
        {
            var testData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "AzureWebJobsStorage", "" }
            };

            using (new TestScopedEnvironmentVariable(testData))
            {
                var webHostConfiguration = new ConfigurationBuilder()
                    .AddEnvironmentVariables()
                    .AddTestSettings()
                    .Build();

                var jobHostConfiguration = new ConfigurationBuilder()
                    .AddEnvironmentVariables()
                    .AddTestSettings()
                    .Build();

                var azureStorageProvider = GetAzureStorageProvider(webHostConfiguration, jobHostConfiguration);
                Assert.Throws<InvalidOperationException>(() => azureStorageProvider.GetBlobContainerClient());

                Assert.False(azureStorageProvider.TryGetBlobServiceClientFromConnection(out BlobServiceClient blobServiceClient, ConnectionStringNames.Storage));
            }
        }

        [Theory]
        [InlineData("ConnectionStrings:AzureWebJobsStorage1")]
        [InlineData("AzureWebJobsStorage1")]
        [InlineData("Storage1")]
        public void TestAzureStorageProvider_JobHostConfigurationWinsConflict(string connectionName)
        {
            var bytes = Encoding.UTF8.GetBytes("someKey");
            var encodedString = Convert.ToBase64String(bytes);

            var testData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { connectionName, $"DefaultEndpointsProtocol=https;AccountName=webHostAccount;AccountKey={encodedString};EndpointSuffix=core.windows.net" },
            };

            using (new TestScopedEnvironmentVariable(testData))
            {
                var webHostConfiguration = new ConfigurationBuilder()
                    .AddEnvironmentVariables()
                    .AddTestSettings()
                    .Build();

                var inMemory = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { connectionName, $"DefaultEndpointsProtocol=https;AccountName=jobHostAccount;AccountKey={encodedString};EndpointSuffix=core.windows.net" },
                };
                var jobHostConfiguration = new ConfigurationBuilder()
                    .AddInMemoryCollection(inMemory)
                    .AddTestSettings()
                    .Build();

                var azureStorageProvider = GetAzureStorageProvider(webHostConfiguration, jobHostConfiguration);
                Assert.True(azureStorageProvider.TryGetBlobServiceClientFromConnection(out BlobServiceClient client, "Storage1"));
                Assert.Equal("jobHostAccount", client.AccountName, ignoreCase: true);
            }
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

        private static IAzureStorageProvider GetAzureStorageProvider(IConfiguration webHostConfiguration, IConfiguration jobHostConfiguration, JobHostInternalStorageOptions storageOptions = null)
        {
            IHost tempHost = new HostBuilder()
                .ConfigureServices(services =>
                {
                    // Override configuration
                    services.AddSingleton(webHostConfiguration);
                    services.AddAzureStorageProvider();

                    var testServiceProvider = new TestScriptHostService(jobHostConfiguration);
                    services.AddSingleton<IScriptHostManager>(testServiceProvider);
                    if (storageOptions != null)
                    {
                        services.AddTransient<IOptions<JobHostInternalStorageOptions>>(s => new OptionsWrapper<JobHostInternalStorageOptions>(storageOptions));
                    }
                }).Build();

            var azureStorageProvider = tempHost.Services.GetRequiredService<IAzureStorageProvider>();
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
