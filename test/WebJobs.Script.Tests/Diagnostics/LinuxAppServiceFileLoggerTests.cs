// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class LinuxAppServiceFileLoggerTests
    {
        private const string LogDirectoryPath = @"C:\temp\logs";
        private const string LogFileName = "FunctionLog";

        [Fact]
        public async void Writes_Logs_to_Files()
        {
            var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            var dirBase = new Mock<DirectoryBase>(MockBehavior.Strict);
            var fileInfoFactory = new Mock<IFileInfoFactory>(MockBehavior.Strict);
            var fileInfoBase = new Mock<FileInfoBase>(MockBehavior.Strict);
            var fileBase = new Mock<FileBase>(MockBehavior.Strict);
            var stream = new Mock<Stream>();
            stream.Setup(s => s.CanWrite).Returns(true);
            var streamWriter = new Mock<StreamWriter>(MockBehavior.Default, stream.Object);

            fileSystem.SetupGet(fs => fs.Directory).Returns(dirBase.Object);
            dirBase.Setup(d => d.CreateDirectory(It.Is<string>(s => string.Equals(s, LogDirectoryPath)))).Returns(new DirectoryInfo(LogDirectoryPath));

            fileSystem.SetupGet(fs => fs.FileInfo).Returns(fileInfoFactory.Object);
            fileInfoFactory.Setup(f => f.FromFileName(It.Is<string>(s => MatchesLogFilePath(s))))
                .Returns(fileInfoBase.Object);
            fileInfoBase.Setup(f => f.Exists).Returns(false);

            fileSystem.SetupGet(fs => fs.File).Returns(fileBase.Object);
            fileBase.Setup(f => f.AppendText(It.Is<string>(s => MatchesLogFilePath(s)))).Returns(streamWriter.Object);

            var expectedLogs = GetLogs();
            for (var i = 0; i < expectedLogs.Count; i++)
            {
                var i1 = i;
                streamWriter.Setup(s => s.WriteLineAsync(It.Is<string>(log => log.Equals(expectedLogs[i1])))).Returns(Task.FromResult(true));
            }

            var fileLogger = new LinuxAppServiceFileLogger(LogFileName, LogDirectoryPath, fileSystem.Object, false, false);

            foreach (var log in GetLogs())
            {
                fileLogger.Log(log);
            }

            await fileLogger.InternalProcessLogQueue();

            fileSystem.VerifyGet(fs => fs.Directory, Times.Once);
            dirBase.Verify(d => d.CreateDirectory(It.Is<string>(s => string.Equals(s, LogDirectoryPath))), Times.Once);

            fileInfoFactory.Verify(f => f.FromFileName(It.Is<string>(s => MatchesLogFilePath(s))), Times.Once);
            fileInfoBase.Verify(f => f.Exists, Times.Once);

            fileSystem.VerifyGet(fs => fs.File, Times.Once);
            fileBase.Verify(f => f.AppendText(It.Is<string>(s => MatchesLogFilePath(s))), Times.Once);
            for (var i = 0; i < expectedLogs.Count; i++)
            {
                var i1 = i;
                streamWriter.Verify(s => s.WriteLineAsync(It.Is<string>(log => log.Equals(expectedLogs[i1]))), Times.Once);
            }
        }

        [Fact]
        public async void Does_Not_Modify_Files_When_No_logs()
        {
            // Expect no methods to be called on ILinuxAppServiceFileSystem
            var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            var fileLogger = new LinuxAppServiceFileLogger(LogFileName, LogDirectoryPath, fileSystem.Object, false, false);
            await fileLogger.InternalProcessLogQueue();
        }

        [Fact]
        public async void Rolls_Files_If_File_Size_Exceeds_Limit()
        {
            var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            var fileLogger = new LinuxAppServiceFileLogger(LogFileName, LogDirectoryPath, fileSystem.Object, false, false);
            var dirBase = new Mock<DirectoryBase>(MockBehavior.Strict);
            var fileInfoFactory = new Mock<IFileInfoFactory>(MockBehavior.Strict);
            var fileInfoBase = new Mock<FileInfoBase>(MockBehavior.Strict);
            var fileBase = new Mock<FileBase>(MockBehavior.Strict);
            var directoryInfoFactory = new Mock<IDirectoryInfoFactory>(MockBehavior.Strict);
            var directoryInfoBase = new Mock<DirectoryInfoBase>(MockBehavior.Strict);

            fileSystem.SetupGet(fs => fs.Directory).Returns(dirBase.Object);
            dirBase.Setup(d => d.CreateDirectory(It.Is<string>(s => string.Equals(s, LogDirectoryPath)))).Returns(new DirectoryInfo(LogDirectoryPath));

            fileSystem.SetupGet(fs => fs.FileInfo).Returns(fileInfoFactory.Object);
            fileInfoFactory.Setup(f => f.FromFileName(It.Is<string>(s => MatchesLogFilePath(s))))
                .Returns(fileInfoBase.Object);
            fileInfoBase.Setup(f => f.Exists).Returns(true);

            fileInfoBase.SetupGet(f => f.Length).Returns((fileLogger.MaxFileSizeMb * 1024 * 1024) + 1);

            fileSystem.SetupGet(fs => fs.File).Returns(fileBase.Object);
            fileBase.Setup(f => f.Move(It.Is<string>(s => MatchesLogFilePath(s)), It.IsAny<string>()));

            fileSystem.SetupGet(fs => fs.DirectoryInfo).Returns(directoryInfoFactory.Object);
            directoryInfoFactory.Setup(d => d.FromDirectoryName(It.Is<string>(s => string.Equals(s, LogDirectoryPath))))
                .Returns(directoryInfoBase.Object);
            directoryInfoBase
                .Setup(d => d.GetFiles(It.Is<string>(s => s.StartsWith(LogFileName)), SearchOption.TopDirectoryOnly))
                .Returns(new FileInfoBase[0]);

            fileLogger.Log("LogMessgae");
            await fileLogger.InternalProcessLogQueue();

            fileSystem.VerifyGet(fs => fs.Directory, Times.Once);
            dirBase.Verify(d => d.CreateDirectory(It.Is<string>(s => string.Equals(s, LogDirectoryPath))), Times.Once);

            fileInfoFactory.Verify(f => f.FromFileName(It.Is<string>(s => MatchesLogFilePath(s))), Times.Once);
            fileInfoBase.Verify(f => f.Exists, Times.Once);

            fileInfoBase.VerifyGet(f => f.Length, Times.Once);

            fileBase.Verify(f => f.Move(It.Is<string>(s => MatchesLogFilePath(s)), It.IsAny<string>()), Times.Once);

            directoryInfoFactory.Verify(d => d.FromDirectoryName(It.Is<string>(s => string.Equals(s, LogDirectoryPath))), Times.Once);
            directoryInfoBase
                .Verify(d => d.GetFiles(It.Is<string>(s => s.StartsWith(LogFileName)), SearchOption.TopDirectoryOnly),
                    Times.Once);
        }

        [Fact]
        public async void Deletes_Oldest_File_If_Exceeds_Limit()
        {
            var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            var fileLogger = new LinuxAppServiceFileLogger(LogFileName, LogDirectoryPath, fileSystem.Object, false, false);
            var dirBase = new Mock<DirectoryBase>(MockBehavior.Strict);
            var fileInfoFactory = new Mock<IFileInfoFactory>(MockBehavior.Strict);
            var fileInfoBase = new Mock<FileInfoBase>(MockBehavior.Strict);
            var fileBase = new Mock<FileBase>(MockBehavior.Strict);
            var directoryInfoFactory = new Mock<IDirectoryInfoFactory>(MockBehavior.Strict);
            var directoryInfoBase = new Mock<DirectoryInfoBase>(MockBehavior.Strict);

            fileSystem.SetupGet(fs => fs.Directory).Returns(dirBase.Object);
            dirBase.Setup(d => d.CreateDirectory(It.Is<string>(s => string.Equals(s, LogDirectoryPath))));

            fileSystem.SetupGet(fs => fs.FileInfo).Returns(fileInfoFactory.Object);
            fileInfoFactory.Setup(f => f.FromFileName(It.Is<string>(s => MatchesLogFilePath(s))))
                .Returns(fileInfoBase.Object);
            fileInfoBase.Setup(f => f.Exists).Returns(true);

            fileInfoBase.SetupGet(f => f.Length).Returns((fileLogger.MaxFileSizeMb * 1024 * 1024) + 1);

            fileSystem.SetupGet(fs => fs.File).Returns(fileBase.Object);
            fileBase.Setup(f => f.Move(It.Is<string>(s => MatchesLogFilePath(s)), It.IsAny<string>()));

            var fileCount = fileLogger.MaxFileCount + 1;
            var fileInfosMock = new Mock<FileInfoBase>[fileCount];
            for (var i = 0; i < fileCount; i++)
            {
                fileInfosMock[i] = new Mock<FileInfoBase>(MockBehavior.Strict);
            }

            fileSystem.SetupGet(fs => fs.DirectoryInfo).Returns(directoryInfoFactory.Object);
            directoryInfoFactory.Setup(d => d.FromDirectoryName(It.Is<string>(s => string.Equals(s, LogDirectoryPath))))
                .Returns(directoryInfoBase.Object);
            directoryInfoBase
                .Setup(d => d.GetFiles(It.Is<string>(s => s.StartsWith(LogFileName)), SearchOption.TopDirectoryOnly))
                .Returns(fileInfosMock.Select(f => f.Object).ToArray);

            fileInfosMock[0].Setup(f => f.Delete());

            fileLogger.Log("LogMessgae");
            await fileLogger.InternalProcessLogQueue();

            fileSystem.SetupGet(fs => fs.Directory).Returns(dirBase.Object);
            dirBase.Setup(d => d.CreateDirectory(It.Is<string>(s => string.Equals(s, LogDirectoryPath))));

            fileSystem.SetupGet(fs => fs.FileInfo).Returns(fileInfoFactory.Object);
            fileInfoFactory.Setup(f => f.FromFileName(It.Is<string>(s => MatchesLogFilePath(s))))
                .Returns(fileInfoBase.Object);
            fileInfoBase.Setup(f => f.Exists).Returns(true);

            fileInfoBase.SetupGet(f => f.Length).Returns((fileLogger.MaxFileSizeMb * 1024 * 1024) + 1);

            fileSystem.SetupGet(fs => fs.File).Returns(fileBase.Object);
            fileBase.Setup(f => f.Move(It.Is<string>(s => MatchesLogFilePath(s)), It.IsAny<string>()));

            fileSystem.SetupGet(fs => fs.DirectoryInfo).Returns(directoryInfoFactory.Object);
            directoryInfoFactory.Setup(d => d.FromDirectoryName(It.Is<string>(s => string.Equals(s, LogDirectoryPath))))
                .Returns(directoryInfoBase.Object);
            directoryInfoBase
                .Setup(d => d.GetFiles(It.Is<string>(s => s.StartsWith(LogFileName)), SearchOption.TopDirectoryOnly))
                .Returns(fileInfosMock.Select(f => f.Object).ToArray);

            fileInfosMock[0].Setup(f => f.Delete());
        }

        [Fact]
        public async void Logs_1_time_in_5_seconds_by_default()
        {
            var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            var dirBase = new Mock<DirectoryBase>(MockBehavior.Strict);
            var fileInfoFactory = new Mock<IFileInfoFactory>(MockBehavior.Strict);
            var fileInfoBase = new Mock<FileInfoBase>(MockBehavior.Strict);
            var fileBase = new Mock<FileBase>(MockBehavior.Strict);
            var stream = new Mock<Stream>();
            stream.Setup(s => s.CanWrite).Returns(true);
            var streamWriter = new Mock<StreamWriter>(MockBehavior.Default, stream.Object);

            fileSystem.SetupGet(fs => fs.Directory).Returns(dirBase.Object);
            dirBase.Setup(d => d.CreateDirectory(It.Is<string>(s => string.Equals(s, LogDirectoryPath)))).Returns(new DirectoryInfo(LogDirectoryPath));

            fileSystem.SetupGet(fs => fs.FileInfo).Returns(fileInfoFactory.Object);
            fileInfoFactory.Setup(f => f.FromFileName(It.Is<string>(s => MatchesLogFilePath(s))))
                .Returns(fileInfoBase.Object);
            fileInfoBase.Setup(f => f.Exists).Returns(false);

            fileSystem.SetupGet(fs => fs.File).Returns(fileBase.Object);
            fileBase.Setup(f => f.AppendText(It.Is<string>(s => MatchesLogFilePath(s)))).Returns(streamWriter.Object);

            var expectedLogs = GetLogs();
            for (var i = 0; i < expectedLogs.Count; i++)
            {
                var i1 = i;
                streamWriter.Setup(s => s.WriteLineAsync(It.Is<string>(log => log.Equals(expectedLogs[i1])))).Returns(Task.FromResult(true));
            }

            var fileLogger = new LinuxAppServiceFileLogger(LogFileName, LogDirectoryPath, fileSystem.Object, false, false);

            foreach (var log in GetLogs())
            {
                fileLogger.Log(log);
            }
            object testState = null;
            _ = fileLogger.ProcessLogQueue(testState);
            // Log not emitted due to 30 second delay
            fileLogger.Log("4th Log, second batch");
            await Task.Delay(5000);
            fileLogger.Stop(new System.TimeSpan(0));

            fileSystem.VerifyGet(fs => fs.Directory, Times.Once);
            dirBase.Verify(d => d.CreateDirectory(It.Is<string>(s => string.Equals(s, LogDirectoryPath))), Times.Once);

            fileInfoFactory.Verify(f => f.FromFileName(It.Is<string>(s => MatchesLogFilePath(s))), Times.Once);
            fileInfoBase.Verify(f => f.Exists, Times.Once);

            fileSystem.VerifyGet(fs => fs.File, Times.Once);
            fileBase.Verify(f => f.AppendText(It.Is<string>(s => MatchesLogFilePath(s))), Times.Once);
            for (var i = 0; i < expectedLogs.Count; i++)
            {
                var i1 = i;
                streamWriter.Verify(s => s.WriteLineAsync(It.Is<string>(log => log.Equals(expectedLogs[i1]))), Times.Once);
            }
        }

        [Fact]
        public async void Logs_3_times_in_5_seconds_with_Backoff()
        {
            var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            var dirBase = new Mock<DirectoryBase>(MockBehavior.Strict);
            var fileInfoFactory = new Mock<IFileInfoFactory>(MockBehavior.Strict);
            var fileInfoBase = new Mock<FileInfoBase>(MockBehavior.Strict);
            var fileBase = new Mock<FileBase>(MockBehavior.Strict);
            var stream = new Mock<Stream>();
            stream.Setup(s => s.CanWrite).Returns(true);
            var streamWriter = new Mock<StreamWriter>(MockBehavior.Default, stream.Object);

            fileSystem.SetupGet(fs => fs.Directory).Returns(dirBase.Object);
            dirBase.Setup(d => d.CreateDirectory(It.Is<string>(s => string.Equals(s, LogDirectoryPath)))).Returns(new DirectoryInfo(LogDirectoryPath));

            fileSystem.SetupGet(fs => fs.FileInfo).Returns(fileInfoFactory.Object);
            fileInfoFactory.Setup(f => f.FromFileName(It.Is<string>(s => MatchesLogFilePath(s))))
                .Returns(fileInfoBase.Object);
            fileInfoBase.Setup(f => f.Exists).Returns(false);

            fileSystem.SetupGet(fs => fs.File).Returns(fileBase.Object);
            fileBase.Setup(f => f.AppendText(It.Is<string>(s => MatchesLogFilePath(s)))).Returns(streamWriter.Object);

            var expectedLogs = GetLogs();
            string expectedLog4 = "4th Log, second batch";
            expectedLogs.Append(expectedLog4);
            string expectedLog5 = "5th log, third batch";
            expectedLogs.Append(expectedLog5);
            for (var i = 0; i < expectedLogs.Count; i++)
            {
                var i1 = i;
                streamWriter.Setup(s => s.WriteLineAsync(It.Is<string>(log => log.Equals(expectedLogs[i1])))).Returns(Task.FromResult(true));
            }

            var fileLogger = new LinuxAppServiceFileLogger(LogFileName, LogDirectoryPath, fileSystem.Object, true, false);

            foreach (var log in GetLogs())
            {
                fileLogger.Log(log);
            }
            object testState = null;

            // Kick off the fileLogger Process and ignore result, call stop. If no delay only one file write
            _ = fileLogger.ProcessLogQueue(testState);
            fileLogger.Log(expectedLog4);
            await Task.Delay(2000);
            fileLogger.Log(expectedLog5);
            await Task.Delay(3000);
            fileLogger.Stop(new System.TimeSpan(0));

            fileSystem.VerifyGet(fs => fs.Directory, Times.Exactly(3));
            dirBase.Verify(d => d.CreateDirectory(It.Is<string>(s => string.Equals(s, LogDirectoryPath))), Times.Exactly(3));

            fileInfoFactory.Verify(f => f.FromFileName(It.Is<string>(s => MatchesLogFilePath(s))), Times.Exactly(3));
            fileInfoBase.Verify(f => f.Exists, Times.Exactly(3));

            fileSystem.VerifyGet(fs => fs.File, Times.Exactly(3));
            fileBase.Verify(f => f.AppendText(It.Is<string>(s => MatchesLogFilePath(s))), Times.Exactly(3));
            for (var i = 0; i < expectedLogs.Count; i++)
            {
                var i1 = i;
                streamWriter.Verify(s => s.WriteLineAsync(It.Is<string>(log => log.Equals(expectedLogs[i1]))), Times.Once);
                if (i >= 3)
                {
                    // Dispose StreamWriter from Using between batches
                    streamWriter.Verify(s => s.Dispose(), Times.Once);
                }
            }
        }

        private static bool MatchesLogFilePath(string filePath)
        {
            if (!string.Equals(".log", Path.GetExtension(filePath)))
            {
                return false;
            }

            if (!string.Equals(LogFileName, Path.GetFileNameWithoutExtension(filePath)))
            {
                return false;
            }

            if (!string.Equals(LogDirectoryPath, Path.GetDirectoryName(filePath)))
            {
                return false;
            }

            return true;
        }

        private static List<string> GetLogs()
        {
            return new List<string>
            {
                "Message 1", "Msg2", "end"
            };
        }
    }
}
