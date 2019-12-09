// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Configuration;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.ChangeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.BreakChangeAnalysis
{
    public class BlobChangeAnalysisStateProviderTests
    {
        private readonly IConfigurationRoot _configuration;
        private readonly IHostIdProvider _hostIdProvider;

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

            var stateProvider = new BlobChangeAnalysisStateProvider(_configuration, _hostIdProvider, NullLogger<BlobChangeAnalysisStateProvider>.Instance);

            ChangeAnalysisState result = await stateProvider.GetCurrentAsync(CancellationToken.None);

            Assert.Equal(lastAnalysisTestTime, result.LastAnalysisTime);
        }

        [Fact]
        public async Task SetTimestamp_PersistsMetadata()
        {
            DateTimeOffset lastAnalysisTestTime = DateTimeOffset.UtcNow;

            var primaryHostMock = new Mock<IPrimaryHostStateProvider>();
            primaryHostMock.Setup(p => p.IsPrimary).Returns(true);

            ICloudBlob analysisBlob = await GetAnalysisBlobReference(_hostIdProvider);

            var stateProvider = new BlobChangeAnalysisStateProvider(_configuration, _hostIdProvider, NullLogger<BlobChangeAnalysisStateProvider>.Instance);

            await stateProvider.SetTimestampAsync(lastAnalysisTestTime, analysisBlob, CancellationToken.None);

            analysisBlob = await analysisBlob.ServiceClient.GetBlobReferenceFromServerAsync(analysisBlob.Uri);

            DateTimeOffset.TryParse(analysisBlob.Metadata[BlobChangeAnalysisStateProvider.AnalysisTimestampMetadataName], out DateTimeOffset result);

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

            var stateProvider = new BlobChangeAnalysisStateProvider(_configuration, _hostIdProvider, NullLogger<BlobChangeAnalysisStateProvider>.Instance);

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

            ICloudBlob analysisBlob = await GetAnalysisBlobReference(_hostIdProvider);
            await analysisBlob.DeleteIfExistsAsync();

            var stateProvider = new BlobChangeAnalysisStateProvider(_configuration, _hostIdProvider, NullLogger<BlobChangeAnalysisStateProvider>.Instance);

            await stateProvider.SetTimestampAsync(lastAnalysisTestTime, analysisBlob, CancellationToken.None);

            analysisBlob = await analysisBlob.ServiceClient.GetBlobReferenceFromServerAsync(analysisBlob.Uri);

            DateTime.TryParse(analysisBlob.Metadata[BlobChangeAnalysisStateProvider.AnalysisTimestampMetadataName], out DateTime result);

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
    }
}
