// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Storage
{
    /// <summary>
    /// Tests whether the StorageClientProvider can properly create a client and send a request
    /// </summary>
    public class TableServiceClientProviderTests
    {
        private const string StorageConnection = "AzureWebJobsStorage";

        private readonly TableServiceClientProvider _tableServiceClientProvider;
        private readonly IConfiguration _configuration;

        public TableServiceClientProviderTests()
        {
            _configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddTestSettings()
                .Build();

            IHost tempHost = new HostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddAzureStorageCoreServices();
                }).Build();

            var componentFactory = tempHost.Services.GetRequiredService<AzureComponentFactory>();
            var logForwarder = tempHost.Services.GetRequiredService<AzureEventSourceLogForwarder>();
            _tableServiceClientProvider = new TableServiceClientProvider(componentFactory, logForwarder);
        }

        [Fact]
        public async Task Create_ConnectionName()
        {
            var client = _tableServiceClientProvider.Create(StorageConnection, _configuration);
            await VerifyServiceAvailable(client);
        }

        [Fact]
        public async Task Create_ConnectionNameWithResolver()
        {
            var resolver = new DefaultNameResolver(_configuration);

            var client = _tableServiceClientProvider.Create(StorageConnection, resolver, _configuration);
            await VerifyServiceAvailable(client);
        }

        [Theory]
        [InlineData("ConnectionStrings:AzureWebJobsStorage")]
        [InlineData("AzureWebJobsStorage")]
        public async Task Create_TryConnectionStringVariants(string keyName)
        {
            var testData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { keyName, Environment.GetEnvironmentVariable(StorageConnection) },
            };
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(testData)
                .AddTestSettings()
                .Build();

            var client = _tableServiceClientProvider.Create(StorageConnection, configuration);
            await VerifyServiceAvailable(client);
        }


        private async Task VerifyServiceAvailable(TableServiceClient client)
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