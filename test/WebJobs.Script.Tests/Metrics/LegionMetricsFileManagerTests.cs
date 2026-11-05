// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Azure.WebJobs.Script.WebHost.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Metrics;

public sealed class LegionMetricsFileManagerTests
{
    private const string MetricsPath = @"C:\metrics";
    private readonly DateTime _now = new(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);
    private readonly TimeSpan _retentionPeriod = TimeSpan.FromMinutes(15);
    private readonly TestLogger<LegionMetricsFileManager> _logger = new();

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
