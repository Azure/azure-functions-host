// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Configuration;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.ChangeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Moq;
using Xunit;
using Microsoft.Azure.WebJobs.StorageProvider.Blobs;

namespace Microsoft.Azure.WebJobs.Script.Tests.BreakChangeAnalysis
{
    public class BlobChangeAnalysisStateProviderTests
    {
        private readonly IConfigurationRoot _configuration;
        private readonly IHostIdProvider _hostIdProvider;
        private readonly HostStorageProvider _hostStorageProvider;

        public BlobChangeAnalysisStateProviderTests()
        {
            _configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddTestSettings()
                .Build();

            var hostIdProviderMock = new Mock<IHostIdProvider>(MockBehavior.Strict);
            hostIdProviderMock.Setup(p => p.GetHostIdAsync(CancellationToken.None))
                .ReturnsAsync($"testhost123{Guid.NewGuid().ToString().Replace("-", string.Empty)}");

            _hostIdProvider = hostIdProviderMock.Object;

            _hostStorageProvider = new HostStorageProvider(_configuration, TestHelpers.GetAzureStorageService<BlobServiceClientProvider>(_configuration));
        }

        [Fact]
        public async Task GetCurrent_ReadsTimestamp()
        {
            DateTimeOffset lastAnalysisTestTime = DateTimeOffset.UtcNow;

            var primaryHostMock = new Mock<IPrimaryHostStateProvider>();
            primaryHostMock.Setup(p => p.IsPrimary).Returns(true);

            CloudBlockBlob analysisBlob = await GetAnalysisBlobReference(_hostIdProvider);
            await analysisBlob.UploadTextAsync(string.Empty);

            analysisBlob.Metadata.Add(BlobChangeAnalysisStateProvider.AnalysisTimestampMetadataName, lastAnalysisTestTime.ToString("O"));
            await analysisBlob.SetMetadataAsync();

            var stateProvider = new BlobChangeAnalysisStateProvider(_configuration, _hostIdProvider, NullLogger<BlobChangeAnalysisStateProvider>.Instance, _hostStorageProvider);

            ChangeAnalysisState result = await stateProvider.GetCurrentAsync(CancellationToken.None);

            Assert.Equal(lastAnalysisTestTime, result.LastAnalysisTime);
        }

        [Fact]
        public async Task SetTimestamp_PersistsMetadata()
        {
            DateTimeOffset lastAnalysisTestTime = DateTimeOffset.UtcNow;

            var primaryHostMock = new Mock<IPrimaryHostStateProvider>();
            primaryHostMock.Setup(p => p.IsPrimary).Returns(true);

            BlobClient analysisBlob = await GetAnalysisBlobClient(_hostIdProvider, _hostStorageProvider);

            var stateProvider = new BlobChangeAnalysisStateProvider(_configuration, _hostIdProvider, NullLogger<BlobChangeAnalysisStateProvider>.Instance, _hostStorageProvider);

            await stateProvider.SetTimestampAsync(lastAnalysisTestTime, analysisBlob, CancellationToken.None);

            var blobPropertiesResponse = await analysisBlob.GetPropertiesAsync();
            DateTimeOffset.TryParse(blobPropertiesResponse.Value.Metadata[BlobChangeAnalysisStateProvider.AnalysisTimestampMetadataName], out DateTimeOffset result);

            Assert.Equal(lastAnalysisTestTime, result);
        }

        [Fact]
        public async Task GetCurrent_WithMissingMetadata_ReturnsExpectedTimestamp()
        {
            var primaryHostMock = new Mock<IPrimaryHostStateProvider>();
            primaryHostMock.Setup(p => p.IsPrimary).Returns(true);

            CloudBlockBlob analysisBlob = await GetAnalysisBlobReference(_hostIdProvider);
            await analysisBlob.DeleteIfExistsAsync();
            await analysisBlob.UploadTextAsync(string.Empty);

            var stateProvider = new BlobChangeAnalysisStateProvider(_configuration, _hostIdProvider, NullLogger<BlobChangeAnalysisStateProvider>.Instance, _hostStorageProvider);

            ChangeAnalysisState result = await stateProvider.GetCurrentAsync(CancellationToken.None);

            Assert.Equal(DateTimeOffset.MinValue, result.LastAnalysisTime);
        }

        [Fact]
        public async Task SetTimestamp_WithNoBlob_PersistsMetadata()
        {
            TimeSpan timePrecision = new TimeSpan(0, 0, 1);
            long dateTimeTicks = (DateTime.UtcNow.Ticks / timePrecision.Ticks) * timePrecision.Ticks;
            DateTime lastAnalysisTestTime = new DateTime(dateTimeTicks);

            var primaryHostMock = new Mock<IPrimaryHostStateProvider>();
            primaryHostMock.Setup(p => p.IsPrimary).Returns(true);

            BlobClient analysisBlob = await GetAnalysisBlobClient(_hostIdProvider, _hostStorageProvider);
            await analysisBlob.DeleteIfExistsAsync();

            var stateProvider = new BlobChangeAnalysisStateProvider(_configuration, _hostIdProvider, NullLogger<BlobChangeAnalysisStateProvider>.Instance, _hostStorageProvider);

            await stateProvider.SetTimestampAsync(lastAnalysisTestTime, analysisBlob, CancellationToken.None);

            var blobPropertiesResponse = await analysisBlob.GetPropertiesAsync();
            DateTimeOffset.TryParse(blobPropertiesResponse.Value.Metadata[BlobChangeAnalysisStateProvider.AnalysisTimestampMetadataName], out DateTimeOffset result);

            Assert.Equal(lastAnalysisTestTime, result);
        }

        private async Task<CloudBlockBlob> GetAnalysisBlobReference(IHostIdProvider hostIdProvider)
        {
            string storageConnectionString = _configuration.GetWebJobsConnectionString(ConnectionStringNames.Storage);

            if (string.IsNullOrEmpty(storageConnectionString) ||
                !CloudStorageAccount.TryParse(storageConnectionString, out CloudStorageAccount account))
            {
                throw new ConfigurationException("Invalid storage account configuration");
            }

            string hostId = await hostIdProvider.GetHostIdAsync(CancellationToken.None);
            string analysisBlobPath = $"changeanalysis/{hostId}/sentinel";

            CloudBlobClient blobClient = account.CreateCloudBlobClient();
            var blobContainer = blobClient.GetContainerReference(ScriptConstants.AzureWebJobsHostsContainerName);

            return blobContainer.GetBlockBlobReference(analysisBlobPath);
        }

        private async Task<BlobClient> GetAnalysisBlobClient(IHostIdProvider hostIdProvider, HostStorageProvider hostStorageProvider)
        {
            if (!hostStorageProvider.TryGetBlobServiceClientFromConnection(out BlobServiceClient blobServiceClient, ConnectionStringNames.Storage))
            {
                throw new ConfigurationException("Invalid storage account configuration");
            }

            string hostId = await hostIdProvider.GetHostIdAsync(CancellationToken.None);
            string analysisBlobPath = $"changeanalysis/{hostId}/sentinel";

            var blobContainerClient = blobServiceClient.GetBlobContainerClient(ScriptConstants.AzureWebJobsHostsContainerName);
            var blobClient = blobContainerClient.GetBlobClient(analysisBlobPath);
            return blobClient;
        }
    }
}
