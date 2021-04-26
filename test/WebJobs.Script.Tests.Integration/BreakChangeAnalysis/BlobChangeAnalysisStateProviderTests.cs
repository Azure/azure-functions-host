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
using Moq;
using Xunit;
using System.IO;

namespace Microsoft.Azure.WebJobs.Script.Tests.BreakChangeAnalysis
{
    public class BlobChangeAnalysisStateProviderTests
    {
        private readonly IConfigurationRoot _configuration;
        private readonly IHostIdProvider _hostIdProvider;
        private readonly IAzureStorageProvider _azureStorageProvider;

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

            _azureStorageProvider = TestHelpers.GetAzureStorageProvider(_configuration);
        }

        [Fact]
        public async Task GetCurrent_ReadsTimestamp()
        {
            DateTimeOffset lastAnalysisTestTime = DateTimeOffset.UtcNow;

            var primaryHostMock = new Mock<IPrimaryHostStateProvider>();
            primaryHostMock.Setup(p => p.IsPrimary).Returns(true);

            BlobClient analysisBlob = await GetAnalysisBlobClient(_hostIdProvider);

            using (Stream stream = new MemoryStream())
            {
                await analysisBlob.UploadAsync(stream, cancellationToken: CancellationToken.None);
            }

            var blobPropertiesResponse = await analysisBlob.GetPropertiesAsync();
            blobPropertiesResponse.Value.Metadata.Add(BlobChangeAnalysisStateProvider.AnalysisTimestampMetadataName, lastAnalysisTestTime.ToString("O"));
            await analysisBlob.SetMetadataAsync(blobPropertiesResponse.Value.Metadata, cancellationToken: CancellationToken.None);

            var stateProvider = new BlobChangeAnalysisStateProvider(_configuration, _hostIdProvider, NullLogger<BlobChangeAnalysisStateProvider>.Instance, _azureStorageProvider);

            ChangeAnalysisState result = await stateProvider.GetCurrentAsync(CancellationToken.None);

            Assert.Equal(lastAnalysisTestTime, result.LastAnalysisTime);
        }

        [Fact]
        public async Task SetTimestamp_PersistsMetadata()
        {
            DateTimeOffset lastAnalysisTestTime = DateTimeOffset.UtcNow;

            var primaryHostMock = new Mock<IPrimaryHostStateProvider>();
            primaryHostMock.Setup(p => p.IsPrimary).Returns(true);

            BlobClient analysisBlob = await GetAnalysisBlobClient(_hostIdProvider);

            var stateProvider = new BlobChangeAnalysisStateProvider(_configuration, _hostIdProvider, NullLogger<BlobChangeAnalysisStateProvider>.Instance, _azureStorageProvider);

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

            BlobClient analysisBlob = await GetAnalysisBlobClient(_hostIdProvider);
            await analysisBlob.DeleteIfExistsAsync();

            using (Stream stream = new MemoryStream())
            {
                await analysisBlob.UploadAsync(stream, cancellationToken: CancellationToken.None);
            }

            var stateProvider = new BlobChangeAnalysisStateProvider(_configuration, _hostIdProvider, NullLogger<BlobChangeAnalysisStateProvider>.Instance, _azureStorageProvider);

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

            BlobClient analysisBlob = await GetAnalysisBlobClient(_hostIdProvider);
            await analysisBlob.DeleteIfExistsAsync();

            var stateProvider = new BlobChangeAnalysisStateProvider(_configuration, _hostIdProvider, NullLogger<BlobChangeAnalysisStateProvider>.Instance, _azureStorageProvider);

            await stateProvider.SetTimestampAsync(lastAnalysisTestTime, analysisBlob, CancellationToken.None);

            var blobPropertiesResponse = await analysisBlob.GetPropertiesAsync();
            DateTimeOffset.TryParse(blobPropertiesResponse.Value.Metadata[BlobChangeAnalysisStateProvider.AnalysisTimestampMetadataName], out DateTimeOffset result);

            Assert.Equal(lastAnalysisTestTime, result);
        }

        private async Task<BlobClient> GetAnalysisBlobClient(IHostIdProvider hostIdProvider)
        {
            if (!_azureStorageProvider.TryGetBlobServiceClientFromConnection(out BlobServiceClient blobServiceClient, ConnectionStringNames.Storage))
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
