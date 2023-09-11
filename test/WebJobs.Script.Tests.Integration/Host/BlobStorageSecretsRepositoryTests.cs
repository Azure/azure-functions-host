// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Threading.Tasks;
using Azure;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Xunit;
using Moq;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using System.Threading;
using Microsoft.Azure.WebJobs.Script.Tests.Integration.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Host
{
    public class BlobStorageSecretsRepositoryTests
    {
        [Fact]
        public async Task BlobRepository_TierSetToArchive_ReadAsync_Logs_DiagnosticEvent()
        {
            var mockBlobStorageProvider = new Mock<IAzureBlobStorageProvider>();
            var mockBlobContainerClient = new Mock<BlobContainerClient>();
            var mockBlobClient = new Mock<BlobClient>();
            var mockBlobServiceClient = new Mock<BlobServiceClient>();

            var exception = new RequestFailedException(409, "Conflict", "BlobArchived", null);
            Response<bool> response = Response.FromValue(true, default);
            Task<Response<bool>> taskResponse = Task.FromResult(response);

            mockBlobStorageProvider
                .Setup(provider => provider.TryCreateBlobServiceClientFromConnection(It.IsAny<string>(), out It.Ref<BlobServiceClient>.IsAny))
                .Returns((string connection, out BlobServiceClient client) =>
                {
                    client = mockBlobServiceClient.Object;
                    return true;
                });

            mockBlobContainerClient
                .Setup(client => client.GetBlobClient(It.IsAny<string>()))
                .Returns(mockBlobClient.Object);

            mockBlobContainerClient
                .Setup(client => client.Exists(default))
                .Returns(response);

            mockBlobServiceClient
                .Setup(client => client.GetBlobContainerClient(It.IsAny<string>()))
                .Returns(mockBlobContainerClient.Object);

            mockBlobClient
                .Setup(client => client.DownloadAsync())
                .Throws(exception);

            mockBlobClient
                .Setup(client => client.ExistsAsync(It.IsAny<CancellationToken>()))
                .Returns(taskResponse);

            // Create logger
            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);
            var logger = loggerFactory.CreateLogger<BlobStorageSecretsRepository>();

            // BlobStorageSecretsRepository settings:
            var environment = new TestEnvironment();
            var secretSentinelDirectoryPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var appName = "Test_test";
            var testFunctionName = "Function1";

            var secretsRepository = new BlobStorageSecretsRepository(
                secretSentinelDirectoryPath,
                ConnectionStringNames.Storage,
                appName,
                logger,
                environment,
                mockBlobStorageProvider.Object);

            await Assert.ThrowsAsync<RequestFailedException>(async () =>
            {
                await secretsRepository.ReadAsync(ScriptSecretsType.Host, testFunctionName);
            });

            DiagnosticEventTestUtils.ValidateThatTheExpectedDiagnosticEventIsPresent(
                loggerProvider,
                Resources.FailedToReadBlobSecretRepositoryTierSetToArchive,
                LogLevel.Error,
                DiagnosticEventConstants.FailedToReadBlobStorageRepositoryHelpLink,
                DiagnosticEventConstants.FailedToReadBlobStorageRepositoryErrorCode
            );
        }
    }
}
