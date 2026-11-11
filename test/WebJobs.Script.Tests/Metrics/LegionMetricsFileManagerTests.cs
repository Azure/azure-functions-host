// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Metrics;

public sealed class LegionMetricsFileManagerTests
{
    private const string MetricsPath = @"C:\metrics";
    private readonly DateTime _now = new(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);
    private readonly TimeSpan _retentionPeriod = TimeSpan.FromMinutes(15);
    private readonly TestLogger<LegionMetricsFileManager> _logger = new();

    private static string StagingPath => Path.Combine(MetricsPath, LegionMetricsFileManager.StagingDirectoryName);

    [Theory]
    [InlineData("unrelated.json")]
    [InlineData("00000000-0000-0000-0000-000000000000.json")]
    [InlineData("not-a-worker-00000000-0000-0000-0000-000000000000.json")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA-not-a-guid.json")]
    [InlineData("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG-00000000-0000-0000-0000-000000000000.json")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA-00000000-0000-0000-0000-000000000000.tmp")]
    public void DeleteExpiredWorkerMetrics_DoesNotDeleteUnrecognizedFiles(string name)
    {
        MockFileSystem fileSystem = new();
        string path = Path.Combine(MetricsPath, name);
        fileSystem.AddFile(path, new MockFileData("{}"));
        fileSystem.File.SetLastWriteTimeUtc(path, _now - _retentionPeriod - TimeSpan.FromSeconds(1));
        LegionMetricsFileManager manager = new(MetricsPath, fileSystem, _logger, 2);

        manager.DeleteExpiredWorkerMetrics(_now, _retentionPeriod);

        Assert.True(fileSystem.File.Exists(path));
    }

    [Fact]
    public void DeleteExpiredWorkerMetrics_UsesLastWriteTimeRatherThanCreationTime()
    {
        MockFileSystem fileSystem = new();
        string path = Path.Combine(MetricsPath, WorkerFileName());
        fileSystem.AddFile(path, new MockFileData("{}"));
        fileSystem.File.SetCreationTimeUtc(path, _now - TimeSpan.FromHours(1));
        fileSystem.File.SetLastWriteTimeUtc(path, _now);
        LegionMetricsFileManager manager = new(MetricsPath, fileSystem, _logger, 2);

        manager.DeleteExpiredWorkerMetrics(_now, _retentionPeriod);

        Assert.True(fileSystem.File.Exists(path));
    }

    [Fact]
    public void DeleteExpiredWorkerMetrics_MissingDirectoryDoesNotCreateIt()
    {
        MockFileSystem fileSystem = new();
        LegionMetricsFileManager manager = new(MetricsPath, fileSystem, _logger, 2);

        manager.DeleteExpiredWorkerMetrics(_now, _retentionPeriod);

        Assert.False(fileSystem.Directory.Exists(MetricsPath));
        Assert.Empty(_logger.GetLogMessages());
    }

    [Fact]
    public void DeleteExpiredWorkerMetrics_DeletionFailureIsLoggedAndOtherFilesAreCleaned()
    {
        Mock<IFileSystem> fileSystem = new();
        Mock<DirectoryInfoBase> directory = new();
        Mock<FileInfoBase> failedFile = CreateExpiredFile();
        Mock<FileInfoBase> otherFile = CreateExpiredFile();
        failedFile.Setup(file => file.Delete()).Throws(new IOException("Deletion failed."));
        fileSystem.Setup(system => system.Directory.Exists(MetricsPath)).Returns(true);
        fileSystem.Setup(system => system.DirectoryInfo.FromDirectoryName(MetricsPath)).Returns(directory.Object);
        directory.Setup(info => info.GetFiles("*.json")).Returns([failedFile.Object, otherFile.Object]);
        LegionMetricsFileManager manager = new(MetricsPath, fileSystem.Object, _logger, 2);

        manager.DeleteExpiredWorkerMetrics(_now, _retentionPeriod);

        failedFile.Verify(file => file.Delete(), Times.Once);
        otherFile.Verify(file => file.Delete(), Times.Once);
        Assert.Contains(_logger.GetLogMessages(), message =>
            message.Level == LogLevel.Error && message.Exception is IOException);
    }

    [Fact]
    public void DeleteExpiredWorkerMetrics_DirectoryFailureIsLogged()
    {
        Mock<IFileSystem> fileSystem = new();
        fileSystem.Setup(system => system.Directory.Exists(MetricsPath)).Returns(true);
        fileSystem.Setup(system => system.DirectoryInfo.FromDirectoryName(MetricsPath)).Throws(new IOException("Directory unavailable."));
        LegionMetricsFileManager manager = new(MetricsPath, fileSystem.Object, _logger, 2);

        manager.DeleteExpiredWorkerMetrics(_now, _retentionPeriod);

        Assert.Contains(_logger.GetLogMessages(), message =>
            message.Level == LogLevel.Error && message.Exception is IOException);
    }

    [Fact]
    public async Task PublishWorkerMetricsAsync_IncreasingCreationTimes_PublishesWithoutRetry()
    {
        MockFileSystem fileSystem = new();
        LegionMetricsFileManager manager = CreateWorkerManager(fileSystem, Sequence(_now, _now.AddSeconds(1)), out Func<int> delayCount);

        Assert.True(await manager.PublishWorkerMetricsAsync(new { Value = 1 }, "worker1"));
        Assert.True(await manager.PublishWorkerMetricsAsync(new { Value = 2 }, "worker2"));

        Assert.Equal(2, fileSystem.Directory.GetFiles(MetricsPath).Length);
        Assert.Empty(fileSystem.Directory.GetFiles(StagingPath));
        Assert.Equal(0, delayCount());
        Assert.DoesNotContain(_logger.GetLogMessages(), message => message.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task PublishWorkerMetricsAsync_RealFileSystem_PreservesObservedCreationTimes()
    {
        using TempDirectory directory = new();
        FileSystem fileSystem = new();
        DateTime stagedTime = DateTime.MinValue;
        LegionMetricsFileManager manager = new(directory.Path, fileSystem, _logger, 2)
        {
            GetCreationTimeUtc = path => stagedTime = fileSystem.File.GetCreationTimeUtc(path)
        };

        Assert.True(await manager.PublishWorkerMetricsAsync(new { Value = 1 }, "worker1"));
        string firstPath = Assert.Single(fileSystem.Directory.GetFiles(directory.Path, "*.json"));
        DateTime firstTime = fileSystem.File.GetCreationTimeUtc(firstPath);
        Assert.Equal(stagedTime, firstTime);

        bool published = await manager.PublishWorkerMetricsAsync(new { Value = 2 }, "worker2");
        string[] files = fileSystem.Directory.GetFiles(directory.Path, "*.json");
        if (published)
        {
            string secondPath = Assert.Single(files.Where(path => !string.Equals(path, firstPath, StringComparison.Ordinal)));
            DateTime secondTime = fileSystem.File.GetCreationTimeUtc(secondPath);
            Assert.Equal(stagedTime, secondTime);
            Assert.True(secondTime > firstTime);
        }
        else
        {
            Assert.Single(files);
        }

        Assert.Empty(fileSystem.Directory.GetFiles(Path.Combine(directory.Path, LegionMetricsFileManager.StagingDirectoryName)));
    }

    [Fact]
    public async Task PublishWorkerMetricsAsync_EqualCreationTime_RetriesUntilLaterCreationTime()
    {
        MockFileSystem fileSystem = new();
        LegionMetricsFileManager manager = CreateWorkerManager(fileSystem, Sequence(_now, _now, _now, _now.AddSeconds(1)), out Func<int> delayCount);

        Assert.True(await manager.PublishWorkerMetricsAsync(new { Value = 1 }, "worker1"));
        Assert.True(await manager.PublishWorkerMetricsAsync(new { Value = 2 }, "worker2"));

        Assert.Equal(2, fileSystem.Directory.GetFiles(MetricsPath).Length);
        Assert.Empty(fileSystem.Directory.GetFiles(StagingPath));
        Assert.Equal(2, delayCount());
        Assert.DoesNotContain(_logger.GetLogMessages(), message => message.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task PublishWorkerMetricsAsync_CreationTimeNeverAdvances_DefersAndRetriesEachPublication()
    {
        MockFileSystem fileSystem = new();
        LegionMetricsFileManager manager = CreateWorkerManager(fileSystem, _ => _now, out Func<int> delayCount);
        manager.MaxWorkerFileAttempts = 3;

        Assert.True(await manager.PublishWorkerMetricsAsync(new { Value = 1 }, "worker1"));
        Assert.False(await manager.PublishWorkerMetricsAsync(new { Value = 2 }, "worker2"));
        Assert.False(await manager.PublishWorkerMetricsAsync(new { Value = 3 }, "worker3"));

        Assert.Single(fileSystem.Directory.GetFiles(MetricsPath));
        Assert.Empty(fileSystem.Directory.GetFiles(StagingPath));
        Assert.Equal(4, delayCount());
        Assert.Equal(2, _logger.GetLogMessages().Count(message => message.Level == LogLevel.Warning));
    }

    [Fact]
    public async Task PublishWorkerMetricsAsync_DeferredFileDoesNotDeleteExistingWorkerFile()
    {
        MockFileSystem fileSystem = new();
        LegionMetricsFileManager manager = CreateWorkerManager(fileSystem, _ => _now, out _, maxFileCount: 1);
        manager.MaxWorkerFileAttempts = 1;

        Assert.True(await manager.PublishWorkerMetricsAsync(new { Value = 1 }, "worker"));
        string firstPath = Assert.Single(fileSystem.Directory.GetFiles(MetricsPath));
        Assert.False(await manager.PublishWorkerMetricsAsync(new { Value = 2 }, "worker"));

        Assert.Equal(firstPath, Assert.Single(fileSystem.Directory.GetFiles(MetricsPath)));
        Assert.Contains("\"Value\":1", fileSystem.File.ReadAllText(firstPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishWorkerMetricsAsync_CreationTimeFailure_LogsErrorAndRemovesStagedFile()
    {
        MockFileSystem fileSystem = new();
        LegionMetricsFileManager manager = CreateWorkerManager(fileSystem, _ => throw new IOException("Unavailable."), out _);

        Assert.False(await manager.PublishWorkerMetricsAsync(new { Value = 1 }, "worker1"));

        Assert.Empty(fileSystem.Directory.GetFiles(MetricsPath));
        Assert.Empty(fileSystem.Directory.GetFiles(StagingPath));
        Assert.Contains(_logger.GetLogMessages(), message =>
            message.Level == LogLevel.Error && message.Exception is IOException);

        manager.GetCreationTimeUtc = _ => _now;
        Assert.True(await manager.PublishWorkerMetricsAsync(new { Value = 1 }, "worker1"));
        Assert.Single(fileSystem.Directory.GetFiles(MetricsPath));
    }

    [Fact]
    public async Task PublishWorkerMetricsAsync_MoveFailureWithConsumedDestination_PausesFurtherPublication()
    {
        MockFileSystem storage = new();
        Mock<FileBase> file = new();
        file.Setup(item => item.CreateText(It.IsAny<string>()))
            .Returns((string path) => storage.File.CreateText(path));
        file.Setup(item => item.Delete(It.IsAny<string>()))
            .Callback((string path) => storage.File.Delete(path));
        file.Setup(item => item.Move(It.IsAny<string>(), It.IsAny<string>()))
            .Callback((string source, string destination) =>
            {
                storage.File.Move(source, destination);
                storage.File.Delete(destination);
                throw new IOException("Move outcome is unknown.");
            });
        Mock<IFileSystem> fileSystem = new();
        fileSystem.SetupGet(system => system.File).Returns(file.Object);
        fileSystem.SetupGet(system => system.Directory).Returns(storage.Directory);
        fileSystem.SetupGet(system => system.DirectoryInfo).Returns(storage.DirectoryInfo);
        LegionMetricsFileManager manager = new(MetricsPath, fileSystem.Object, _logger, 2)
        {
            GetCreationTimeUtc = _ => _now
        };

        Assert.False(await manager.PublishWorkerMetricsAsync(new { Value = 1 }, "worker1"));
        Assert.False(await manager.PublishWorkerMetricsAsync(new { Value = 1 }, "worker1"));
        Assert.False(await manager.PublishWorkerMetricsAsync(new { Value = 2 }, "worker2"));

        file.Verify(item => item.Move(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        Assert.Empty(storage.Directory.GetFiles(MetricsPath));
        Assert.Empty(storage.Directory.GetFiles(StagingPath));
        Assert.Contains(_logger.GetLogMessages(), message =>
            message.Level == LogLevel.Error &&
            message.Exception is IOException &&
            message.FormattedMessage.Contains("Pausing Worker metrics publication", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublishWorkerMetricsAsync_NoMetricsPath_DoesNotWriteFiles()
    {
        MockFileSystem fileSystem = new();
        LegionMetricsFileManager manager = new(null, fileSystem, _logger, 2);

        Assert.True(await manager.PublishWorkerMetricsAsync(new { Value = 1 }, "worker1"));

        Assert.False(fileSystem.Directory.Exists(MetricsPath));
    }

    [Fact]
    public void DeleteExpiredWorkerMetrics_DeletesOnlyStaleStagedFiles()
    {
        MockFileSystem fileSystem = new();
        string stalePath = Path.Combine(StagingPath, "stale.tmp");
        string recentPath = Path.Combine(StagingPath, "recent.tmp");
        fileSystem.AddFile(stalePath, new MockFileData("{}"));
        fileSystem.AddFile(recentPath, new MockFileData("{}"));
        fileSystem.File.SetLastWriteTimeUtc(stalePath, _now - TimeSpan.FromMinutes(2));
        fileSystem.File.SetLastWriteTimeUtc(recentPath, _now - TimeSpan.FromSeconds(30));
        LegionMetricsFileManager manager = new(MetricsPath, fileSystem, _logger, 2);

        manager.DeleteExpiredWorkerMetrics(_now, _retentionPeriod);

        Assert.False(fileSystem.File.Exists(stalePath));
        Assert.True(fileSystem.File.Exists(recentPath));
    }

    [Fact]
    public async Task DeleteExpiredWorkerMetrics_DoesNotDeleteAnActiveStagedFile()
    {
        MockFileSystem fileSystem = new();
        using ManualResetEventSlim staged = new();
        using ManualResetEventSlim resume = new();
        LegionMetricsFileManager manager = CreateWorkerManager(fileSystem, _ =>
        {
            staged.Set();
            resume.Wait();
            return _now;
        }, out _);

        Task<bool> publishTask = Task.Run(() => manager.PublishWorkerMetricsAsync(new { Value = 1 }, "worker"));
        try
        {
            Assert.True(staged.Wait(TimeSpan.FromSeconds(10)));
            manager.DeleteExpiredWorkerMetrics(_now, _retentionPeriod);
            Assert.Single(fileSystem.Directory.GetFiles(StagingPath));
        }
        finally
        {
            resume.Set();
        }

        Assert.True(await publishTask);
        Assert.Empty(fileSystem.Directory.GetFiles(StagingPath));
    }

    private LegionMetricsFileManager CreateWorkerManager(MockFileSystem fileSystem, Func<string, DateTime> getCreationTimeUtc, out Func<int> delayCount, int maxFileCount = 10)
    {
        int delays = 0;
        delayCount = () => delays;
        LegionMetricsFileManager manager = new(MetricsPath, fileSystem, _logger, maxFileCount)
        {
            GetCreationTimeUtc = getCreationTimeUtc,
            DelayAsync = _ =>
            {
                delays++;
                return Task.CompletedTask;
            }
        };

        return manager;
    }

    private static Func<string, DateTime> Sequence(params DateTime[] values)
    {
        Queue<DateTime> queue = new(values);

        return _ => queue.Dequeue();
    }

    private Mock<FileInfoBase> CreateExpiredFile()
    {
        Mock<FileInfoBase> file = new();
        string name = WorkerFileName();
        file.SetupGet(info => info.Name).Returns(name);
        file.SetupGet(info => info.FullName).Returns(Path.Combine(MetricsPath, name));
        file.SetupGet(info => info.LastWriteTimeUtc).Returns(_now - _retentionPeriod - TimeSpan.FromSeconds(1));

        return file;
    }

    private static string WorkerFileName() => $"{new string('A', 64)}-{Guid.NewGuid():D}.json";
}
