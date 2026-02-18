// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Scale
{
    /// <summary>
    /// Unit tests for <see cref="TableStorageScaleMetricsRepository"/>.
    /// Integration tests that verify end-to-end behavior with real Azure Storage
    /// are in the WebJobs.Script.Tests.Integration project.
    /// </summary>
    public class TableStorageScaleMetricsRepositoryTests
    {
        [Theory]
        [InlineData((int)HttpStatusCode.TooManyRequests, true)] // 429
        [InlineData((int)HttpStatusCode.InternalServerError, true)] // 500
        [InlineData((int)HttpStatusCode.ServiceUnavailable, true)] // 503
        [InlineData((int)HttpStatusCode.GatewayTimeout, true)] // 504
        [InlineData((int)HttpStatusCode.NotFound, false)] // 404 - not transient
        [InlineData((int)HttpStatusCode.BadRequest, false)] // 400 - not transient
        [InlineData((int)HttpStatusCode.Unauthorized, false)] // 401 - not transient
        [InlineData((int)HttpStatusCode.Forbidden, false)] // 403 - not transient
        [InlineData((int)HttpStatusCode.Conflict, false)] // 409 - not transient
        [InlineData((int)HttpStatusCode.RequestTimeout, false)] // 408 - not in transient list
        public void IsTransientStorageError_ReturnsExpectedResult(int statusCode, bool expectedResult)
        {
            var exception = new RequestFailedException(statusCode, "Test error message", "TestErrorCode", null);

            var result = TableStorageScaleMetricsRepository.IsTransientStorageError(exception);

            Assert.Equal(expectedResult, result);
        }

        [Fact]
        public void IsTransientStorageError_WithNullException_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => TableStorageScaleMetricsRepository.IsTransientStorageError(null));
        }

        [Theory]
        [InlineData(HttpStatusCode.TooManyRequests, "The server is busy")]
        [InlineData(HttpStatusCode.ServiceUnavailable, "Service is temporarily unavailable")]
        [InlineData(HttpStatusCode.InternalServerError, "Internal server error")]
        [InlineData(HttpStatusCode.GatewayTimeout, "Gateway timeout")]
        public void IsTransientStorageError_TransientStatusCodes_ReturnsTrue(HttpStatusCode statusCode, string message)
        {
            var exception = new RequestFailedException((int)statusCode, message);

            var result = TableStorageScaleMetricsRepository.IsTransientStorageError(exception);

            Assert.True(result, $"Expected {statusCode} to be identified as transient");
        }

        [Theory]
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.Conflict)]
        [InlineData(HttpStatusCode.PreconditionFailed)]
        public void IsTransientStorageError_NonTransientStatusCodes_ReturnsFalse(HttpStatusCode statusCode)
        {
            var exception = new RequestFailedException((int)statusCode, "Error");

            var result = TableStorageScaleMetricsRepository.IsTransientStorageError(exception);

            Assert.False(result, $"Expected {statusCode} to NOT be identified as transient");
        }
    }

    /// <summary>
    /// Tests for retry behavior in <see cref="TableStorageScaleMetricsRepository"/>.
    /// These tests verify that transient errors trigger retries with appropriate logging.
    /// </summary>
    public class TableStorageScaleMetricsRepositoryRetryTests
    {
        private readonly TestLoggerProvider _loggerProvider;
        private readonly Mock<IHostIdProvider> _hostIdProviderMock;
        private readonly Mock<IAzureTableStorageProvider> _storageProviderMock;
        private readonly Mock<TableServiceClient> _tableServiceClientMock;
        private readonly Mock<TableClient> _tableClientMock;
        private readonly ScaleOptions _scaleOptions;

        public TableStorageScaleMetricsRepositoryRetryTests()
        {
            _loggerProvider = new TestLoggerProvider();
            _hostIdProviderMock = new Mock<IHostIdProvider>(MockBehavior.Strict);
            _hostIdProviderMock.Setup(p => p.GetHostIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync("testhostid");

            _tableClientMock = new Mock<TableClient>();
            _tableServiceClientMock = new Mock<TableServiceClient>();
            _tableServiceClientMock.Setup(s => s.GetTableClient(It.IsAny<string>())).Returns(_tableClientMock.Object);

            _storageProviderMock = new Mock<IAzureTableStorageProvider>();
            TableServiceClient outClient = _tableServiceClientMock.Object;
            _storageProviderMock.Setup(p => p.TryCreateHostingTableServiceClient(out outClient)).Returns(true);

            _scaleOptions = new ScaleOptions { MetricsPurgeEnabled = false };
        }

        private TableStorageScaleMetricsRepository CreateRepository()
        {
            ILoggerFactory loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);
            return new TableStorageScaleMetricsRepository(
                _hostIdProviderMock.Object,
                new OptionsWrapper<ScaleOptions>(_scaleOptions),
                loggerFactory,
                _storageProviderMock.Object);
        }

        [Fact]
        public async Task ExecuteBatchSafeAsync_NoErrors_SucceedsWithoutRetry()
        {
            _tableClientMock
                .Setup(c => c.SubmitTransactionAsync(
                    It.IsAny<IEnumerable<TableTransactionAction>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(
                    new List<Response>() as IReadOnlyList<Response>,
                    Mock.Of<Response>()));

            var repository = CreateRepository();
            var batch = new List<TableTransactionAction>
            {
                new TableTransactionAction(TableTransactionActionType.Add, new TableEntity("pk", "rk"))
            };

            await repository.ExecuteBatchSafeAsync(batch);

            _tableClientMock.Verify(
                c => c.SubmitTransactionAsync(
                    It.IsAny<IEnumerable<TableTransactionAction>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            var logs = _loggerProvider.GetAllLogMessages();
            var retryLog = logs.FirstOrDefault(l => l.FormattedMessage.Contains("Transient storage error"));
            Assert.Null(retryLog);
        }

        [Fact]
        public async Task ExecuteBatchSafeAsync_TransientError_RetriesAndLogsWarning()
        {
            int callCount = 0;
            _tableClientMock
                .Setup(c => c.SubmitTransactionAsync(
                    It.IsAny<IEnumerable<TableTransactionAction>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        // First call throws transient error
                        throw new RequestFailedException(503, "The server is busy");
                    }
                    // Subsequent calls succeed
                    return Response.FromValue(
                        new List<Response>() as IReadOnlyList<Response>,
                        Mock.Of<Response>());
                });

            var repository = CreateRepository();
            var batch = new List<TableTransactionAction>
            {
                new TableTransactionAction(TableTransactionActionType.Add, new TableEntity("pk", "rk"))
            };

            await repository.ExecuteBatchSafeAsync(batch);

            _tableClientMock.Verify(
                c => c.SubmitTransactionAsync(
                    It.IsAny<IEnumerable<TableTransactionAction>>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));

            var logs = _loggerProvider.GetAllLogMessages();
            var retryLog = logs.FirstOrDefault(l => l.FormattedMessage.Contains("Transient storage error during scale metrics operation"));
            Assert.NotNull(retryLog);
            Assert.Equal(LogLevel.Warning, retryLog.Level);
            Assert.Contains("Status: 503", retryLog.FormattedMessage);
            Assert.Contains("Attempt: 1", retryLog.FormattedMessage);
        }

        [Fact]
        public async Task ExecuteBatchSafeAsync_NonTransientError_DoesNotRetry()
        {
            _tableClientMock
                .Setup(c => c.SubmitTransactionAsync(
                    It.IsAny<IEnumerable<TableTransactionAction>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(400, "Bad request"));

            var repository = CreateRepository();
            var batch = new List<TableTransactionAction>
            {
                new TableTransactionAction(TableTransactionActionType.Add, new TableEntity("pk", "rk"))
            };

            await Assert.ThrowsAsync<RequestFailedException>(() => repository.ExecuteBatchSafeAsync(batch));

            // Should only be called once - no retry for non-transient errors
            _tableClientMock.Verify(
                c => c.SubmitTransactionAsync(
                    It.IsAny<IEnumerable<TableTransactionAction>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            // No retry warning should be logged
            var logs = _loggerProvider.GetAllLogMessages();
            var retryLog = logs.FirstOrDefault(l => l.FormattedMessage.Contains("Transient storage error"));
            Assert.Null(retryLog);
        }

        [Fact]
        public async Task ExecuteBatchSafeAsync_MultipleTransientErrors_RetriesMultipleTimes()
        {
            int callCount = 0;
            _tableClientMock
                .Setup(c => c.SubmitTransactionAsync(
                    It.IsAny<IEnumerable<TableTransactionAction>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    if (callCount <= 3)
                    {
                        // First 3 calls throw transient errors
                        throw new RequestFailedException(429, "Too many requests");
                    }
                    return Response.FromValue(
                        new List<Response>() as IReadOnlyList<Response>,
                        Mock.Of<Response>());
                });

            var repository = CreateRepository();
            var batch = new List<TableTransactionAction>
            {
                new TableTransactionAction(TableTransactionActionType.Add, new TableEntity("pk", "rk"))
            };

            await repository.ExecuteBatchSafeAsync(batch);

            _tableClientMock.Verify(
                c => c.SubmitTransactionAsync(
                    It.IsAny<IEnumerable<TableTransactionAction>>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(4));

            var logs = _loggerProvider.GetAllLogMessages();
            var retryLogs = logs.Where(l => l.FormattedMessage.Contains("Transient storage error")).ToList();
            Assert.Equal(3, retryLogs.Count);
        }

        [Fact]
        public async Task ExecuteBatchSafeAsync_ExceedsMaxRetries_ThrowsException()
        {
            _tableClientMock
                .Setup(c => c.SubmitTransactionAsync(
                    It.IsAny<IEnumerable<TableTransactionAction>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(503, "The server is busy"));

            var repository = CreateRepository();
            var batch = new List<TableTransactionAction>
            {
                new TableTransactionAction(TableTransactionActionType.Add, new TableEntity("pk", "rk"))
            };

            await Assert.ThrowsAsync<RequestFailedException>(() => repository.ExecuteBatchSafeAsync(batch));

            // DefaultOperationRetries is 5, so we expect 6 total calls (1 initial + 5 retries)
            _tableClientMock.Verify(
                c => c.SubmitTransactionAsync(
                    It.IsAny<IEnumerable<TableTransactionAction>>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(6));

            var logs = _loggerProvider.GetAllLogMessages();
            var retryLogs = logs.Where(l => l.FormattedMessage.Contains("Transient storage error")).ToList();
            Assert.Equal(5, retryLogs.Count);
        }

        [Fact]
        public async Task ExecuteBatchSafeAsync_TableNotFound_CreatesTableOnlyOnce()
        {
            // This test verifies that when table doesn't exist and is created,
            // subsequent retries due to transient errors don't try to create the table again.
            int submitCallCount = 0;
            int createTableCallCount = 0;

            _tableClientMock
                .Setup(c => c.SubmitTransactionAsync(
                    It.IsAny<IEnumerable<TableTransactionAction>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    submitCallCount++;
                    if (submitCallCount == 1)
                    {
                        // First call: table doesn't exist
                        throw new RequestFailedException(404, "Table not found", "TableNotFound", null);
                    }
                    if (submitCallCount == 2)
                    {
                        // Second call (after table creation): transient error
                        throw new RequestFailedException(503, "The server is busy");
                    }
                    // Third call: success
                    return Response.FromValue(
                        new List<Response>() as IReadOnlyList<Response>,
                        Mock.Of<Response>());
                });

            // Mock TableServiceClient.QueryAsync to return empty (table doesn't exist)
            var emptyPageable = AsyncPageable<TableItem>.FromPages(Array.Empty<Page<TableItem>>());
            _tableServiceClientMock
                .Setup(s => s.QueryAsync(
                    It.IsAny<System.Linq.Expressions.Expression<Func<TableItem, bool>>>(),
                    It.IsAny<int?>(),
                    It.IsAny<CancellationToken>()))
                .Returns(emptyPageable);

            _tableClientMock
                .Setup(c => c.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    createTableCallCount++;
                    return Mock.Of<Response<TableItem>>();
                });

            var repository = CreateRepository();
            var batch = new List<TableTransactionAction>
            {
                new TableTransactionAction(TableTransactionActionType.Add, new TableEntity("pk", "rk"))
            };

            await repository.ExecuteBatchSafeAsync(batch);

            // Table creation should only be called once, not on subsequent retry
            Assert.Equal(1, createTableCallCount);

            // We expect: 1st call (table not found) -> create table -> 2nd call (transient error) -> retry -> 3rd call (success)
            Assert.Equal(3, submitCallCount);

            // Verify retry logging for the transient error
            var logs = _loggerProvider.GetAllLogMessages();
            var retryLog = logs.FirstOrDefault(l => l.FormattedMessage.Contains("Transient storage error"));
            Assert.NotNull(retryLog);
            Assert.Contains("Status: 503", retryLog.FormattedMessage);
        }
    }
}
