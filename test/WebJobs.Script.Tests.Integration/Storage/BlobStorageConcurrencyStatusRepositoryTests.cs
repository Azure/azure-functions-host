// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.EndToEndTests
{
    /// <summary>
    /// TODO: TEMP - this temporary implementation should be removed once https://github.com/Azure/azure-webjobs-sdk/issues/2710 is addressed.
    /// </summary>
    public class BlobStorageConcurrencyStatusRepositoryTests
    {
        private const string TestHostId = "test123";
        private readonly BlobStorageConcurrencyStatusRepository _repository;
        private readonly LoggerFactory _loggerFactory;
        private readonly TestLoggerProvider _loggerProvider;
        private readonly HostConcurrencySnapshot _testSnapshot;
        private readonly Mock<IHostIdProvider> _mockHostIdProvider;

        public BlobStorageConcurrencyStatusRepositoryTests()
        {
            _testSnapshot = new HostConcurrencySnapshot
            {
                NumberOfCores = 4,
                Timestamp = DateTime.UtcNow
            };
            _testSnapshot.FunctionSnapshots = new Dictionary<string, FunctionConcurrencySnapshot>
            {
                { "function0", new FunctionConcurrencySnapshot { Concurrency = 5 } },
                { "function1", new FunctionConcurrencySnapshot { Concurrency = 10 } },
                { "function2", new FunctionConcurrencySnapshot { Concurrency = 15 } }
            };

            _loggerFactory = new LoggerFactory();
            _loggerProvider = new TestLoggerProvider();
            _loggerFactory.AddProvider(_loggerProvider);

            _mockHostIdProvider = new Mock<IHostIdProvider>(MockBehavior.Strict);
            _mockHostIdProvider.Setup(p => p.GetHostIdAsync(CancellationToken.None)).ReturnsAsync(TestHostId);

            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();
            var azureStorageProvider = TestHelpers.GetAzureStorageProvider(config);

            _repository = new BlobStorageConcurrencyStatusRepository(_mockHostIdProvider.Object, _loggerFactory, azureStorageProvider);
        }

        [Fact]
        public async Task GetContainerClientAsync_ReturnsExpectedContainer()
        {
            BlobContainerClient blobContainerClient = await _repository.GetContainerClientAsync(CancellationToken.None);
            Assert.Equal(ScriptConstants.AzureWebJobsHostsContainerName, blobContainerClient.Name);
        }

        [Fact]
        public async Task GetBlobPathAsync_ReturnsExpectedPath()
        {
            string path = await _repository.GetBlobPathAsync(CancellationToken.None);

            Assert.Equal($"concurrency/{TestHostId}/concurrencyStatus.json", path);
        }

        [Fact]
        public async Task WriteAsync_WritesExpectedBlob()
        {
            await DeleteTestBlobsAsync();

            var path = await _repository.GetBlobPathAsync(CancellationToken.None);
            BlobContainerClient blobContainerClient = await _repository.GetContainerClientAsync(CancellationToken.None);
            BlobClient blobClient = blobContainerClient.GetBlobClient(path);
            bool exists = await blobClient.ExistsAsync();
            Assert.False(exists);

            await _repository.WriteAsync(_testSnapshot, CancellationToken.None);

            exists = await blobClient.ExistsAsync();
            Assert.True(exists);

            string content;
            var downloadResponse = await blobClient.DownloadAsync();
            using (StreamReader reader = new StreamReader(downloadResponse.Value.Content))
            {
                content = reader.ReadToEnd();
            }

            var result = JsonConvert.DeserializeObject<HostConcurrencySnapshot>(content);

            Assert.True(_testSnapshot.Equals(result));

            // upload again and ensure the existing blob is replaced
            _testSnapshot.NumberOfCores += 2;
            await _repository.WriteAsync(_testSnapshot, CancellationToken.None);
            downloadResponse = await blobClient.DownloadAsync();
            using (StreamReader reader = new StreamReader(downloadResponse.Value.Content))
            {
                content = reader.ReadToEnd();
            }
            result = JsonConvert.DeserializeObject<HostConcurrencySnapshot>(content);
            Assert.Equal(_testSnapshot.NumberOfCores, result.NumberOfCores);

        }

        [Fact]
        public async Task ReadAsync_ReadsExpectedBlob()
        {
            await DeleteTestBlobsAsync();

            string path = await _repository.GetBlobPathAsync(CancellationToken.None);
            BlobContainerClient blobContainerClient = await _repository.GetContainerClientAsync(CancellationToken.None);
            BlobClient blobClient = blobContainerClient.GetBlobClient(path);

            string content = JsonConvert.SerializeObject(_testSnapshot);
            using (Stream stream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
            {
                await blobClient.UploadAsync(stream, overwrite: true);
            }

            var result = await _repository.ReadAsync(CancellationToken.None);

            Assert.True(_testSnapshot.Equals(result));
        }

        [Fact]
        public async Task ReadAsync_NoSnapshot_ReturnsNull()
        {
            await DeleteTestBlobsAsync();

            var result = await _repository.ReadAsync(CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task NoStorageConnection_HandledGracefully()
        {
            IConfiguration configuration = new ConfigurationBuilder().Build();
            var azureStorageProvider = TestHelpers.GetAzureStorageProvider(configuration);
            var localRepository = new BlobStorageConcurrencyStatusRepository(_mockHostIdProvider.Object, _loggerFactory, azureStorageProvider);

            var container = await localRepository.GetContainerClientAsync(CancellationToken.None);
            Assert.Null(container);

            await localRepository.WriteAsync(new HostConcurrencySnapshot(), CancellationToken.None);

            var snapshot = await localRepository.ReadAsync(CancellationToken.None);
            Assert.Null(snapshot);
        }

        private async Task DeleteTestBlobsAsync()
        {
            BlobContainerClient blobContainerClient = await _repository.GetContainerClientAsync(CancellationToken.None);
            var blobItems = blobContainerClient.GetBlobsByHierarchyAsync(prefix: $"concurrency/{TestHostId}");
            await foreach (var blob in blobItems)
            {
                BlobClient blobClient = blobContainerClient.GetBlobClient(blob.Blob.Name);
                await blobClient.DeleteAsync();
            }
        }
    }
}
