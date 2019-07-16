// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FileWriterTests
    {
        private string _logFilePath;

        public FileWriterTests()
        {
            _logFilePath = Path.Combine(Path.GetTempPath(), "WebJobs.Script.Tests", "FileWriterTests");

            if (Directory.Exists(_logFilePath))
            {
                Directory.Delete(_logFilePath, recursive: true);
            }
        }

        [Fact]
        public async Task MultipleConcurrentInstances_WritesAreSerialized()
        {
            // first ensure the file exists by writing a single entry
            // this ensures all the instances below will be operating on
            // the same file
            WriteLogs(_logFilePath, 1);

            // start 3 concurrent instances
            int numLines = 100;
            await Task.WhenAll(
                Task.Run(() => WriteLogs(_logFilePath, numLines)),
                Task.Run(() => WriteLogs(_logFilePath, numLines)),
                Task.Run(() => WriteLogs(_logFilePath, numLines)));

            string logFile = Directory.EnumerateFiles(_logFilePath).Single();
            string[] fileLines = File.ReadAllLines(logFile);
            Assert.Equal((3 * numLines) + 1, fileLines.Length);
        }

        [Fact]
        public async Task SetLogFile_PurgesOldLogFiles()
        {
            var directory = new DirectoryInfo(_logFilePath);
            directory.Create();

            // below test expects the retention days to be set to 1
            Assert.Equal(1, FileWriter.LastModifiedCutoffDays);

            // create some log files
            var logFiles = new List<FileInfo>();
            for (int i = 0; i < 5; i++)
            {
                string fileName = string.Format("{0}-{1}.log", i, FileWriter.GetInstanceId());
                string path = Path.Combine(_logFilePath, fileName);
                File.WriteAllText(path, "Test Logs");
                logFiles.Add(new FileInfo(path));
            }

            // push all but the last file over size limit
            string maxLog = TestHelpers.NewRandomString((int)FileWriter.MaxLogFileSizeBytes + 1);
            File.AppendAllText(logFiles[3].FullName, maxLog);
            File.AppendAllText(logFiles[2].FullName, maxLog);
            File.AppendAllText(logFiles[1].FullName, maxLog);
            File.AppendAllText(logFiles[0].FullName, maxLog);

            // mark all the files as old to simulate the passage of time
            File.SetLastWriteTime(logFiles[4].FullName, DateTime.Now.Subtract(TimeSpan.FromDays(2)));
            File.SetLastWriteTime(logFiles[3].FullName, DateTime.Now.Subtract(TimeSpan.FromDays(2)));
            File.SetLastWriteTime(logFiles[2].FullName, DateTime.Now.Subtract(TimeSpan.FromDays(3)));
            File.SetLastWriteTime(logFiles[1].FullName, DateTime.Now.Subtract(TimeSpan.FromDays(4)));
            File.SetLastWriteTime(logFiles[0].FullName, DateTime.Now.Subtract(TimeSpan.FromDays(5)));

            var files = directory.GetFiles().OrderByDescending(p => p.LastWriteTime).ToArray();
            Assert.Equal(5, files.Length);

            // now cause a new log file to be created by writing a huge
            // log pushing the current file over limit
            var fileWriter = new FileWriter(_logFilePath);
            fileWriter.AppendLine(maxLog);

            // wait for the new file to be created and the old files to be purged
            await TestHelpers.Await(() =>
            {
                files = directory.GetFiles().OrderByDescending(p => p.LastWriteTime).ToArray();
                return files.Length == 2;
            }, timeout: 5000);

            // expect only 2 files to remain - the new file we just created, as well
            // as the oversize file we just wrote to last (it has a new timestamp now so
            // wasn't purged)
            Assert.True(files[1].Length > FileWriter.MaxLogFileSizeBytes);

            // expect the new file to be empty because everything was flushed
            // to the previous file before it was created
            var fileLines = File.ReadAllLines(files[0].FullName);
            Assert.Equal(0, fileLines.Length);

            // make sure the new log is written to the new file
            fileWriter.AppendLine("test message");
            fileWriter.Flush();
            fileLines = File.ReadAllLines(files[0].FullName);
            Assert.Equal(1, fileLines.Length);
        }

        [Fact]
        public void Constructor_EmptyDirectory_DelayCreatesLogFile()
        {
            var directory = new DirectoryInfo(_logFilePath);
            directory.Create();

            var fileWriter = new FileWriter(_logFilePath);

            var files = directory.GetFiles().OrderByDescending(p => p.LastWriteTime).ToArray();
            Assert.Equal(0, files.Length);

            fileWriter.AppendLine("Test log");
            fileWriter.Flush();

            // log file is delay created
            files = directory.GetFiles().OrderByDescending(p => p.LastWriteTime).ToArray();
            Assert.Equal(1, files.Length);
        }

        [Fact]
        public async Task SetLogFile_EmptyDirectory_MultipleInstances_Staggered_CreatesOneFile()
        {
            var writers = new List<FileWriter>();
            for (int i = 0; i < 5; i++)
            {
                writers.Add(new FileWriter(_logFilePath));
                await Task.Delay(400);
            }

            writers.ForEach(p => p.AppendLine($"test message"));
            writers.ForEach(p => p.Flush());

            var directory = new DirectoryInfo(_logFilePath);
            var files = directory.GetFiles().OrderByDescending(p => p.LastWriteTime).ToArray();
            Assert.Equal(1, files.Length);

            var fileLines = File.ReadAllLines(files.Single().FullName);
            Assert.Equal(5, fileLines.Length);
        }

        [Fact]
        public async Task SetLogFile_EmptyDirectory_MultipleInstances_Concurrent_CreatesOneFile()
        {
            var tasks = new List<Task>();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(() => WriteLogs(_logFilePath, 10)));
            }
            await Task.WhenAll(tasks);

            var directory = new DirectoryInfo(_logFilePath);
            var files = directory.GetFiles().OrderByDescending(p => p.LastWriteTime).ToArray();
            Assert.Equal(1, files.Length);

            var fileLines = File.ReadAllLines(files.Single().FullName);
            Assert.Equal(100, fileLines.Length);
        }

        [Fact]
        public void Flush_ReusesLastFile()
        {
            WriteLogs(_logFilePath, 3);

            var directory = new DirectoryInfo(_logFilePath);
            int count = directory.EnumerateFiles().Count();
            Assert.Equal(1, count);

            string logFile = directory.EnumerateFiles().First().FullName;
            string[] fileLines = File.ReadAllLines(logFile);
            Assert.Equal(3, fileLines.Length);
        }

        [Fact]
        public async Task Flush_ThrottlesLogs()
        {
            int numLogs = 10000;
            int numIterations = 3;
            for (int i = 0; i < numIterations; i++)
            {
                Task ignore = Task.Run(() => WriteLogs(_logFilePath, numLogs));
                await Task.Delay(1000);
            }

            DirectoryInfo directory = new DirectoryInfo(_logFilePath);
            string logFile = directory.EnumerateFiles().First().FullName;
            string[] fileLines = File.ReadAllLines(logFile);
            Assert.True(fileLines.Length == ((FileWriter.MaxLogLinesPerFlushInterval * numIterations) + 3));
            Assert.Equal(3, fileLines.Count(p => p.Contains("Log output threshold exceeded.")));
        }

        [Fact]
        public async Task Flush_LogFileDeleted_CreatesNewFile()
        {
            var fileWriter = new FileWriter(_logFilePath);

            fileWriter.AppendLine("test trace");
            fileWriter.Flush();

            var directory = new DirectoryInfo(_logFilePath);
            var firstLogFile = directory.EnumerateFiles().Single();

            // delete the first log file to force another one to be
            // created
            File.Delete(firstLogFile.FullName);

            // wait at least a second to ensure the file gets a distinct timestamp
            await TestHelpers.Await(() => !File.Exists(firstLogFile.FullName));
            await Task.Delay(1000);

            fileWriter.AppendLine("test trace");
            fileWriter.Flush();

            var secondLogFile = directory.EnumerateFiles().Single();

            // verify that a new log file was created with a different
            // timestamp
            Assert.NotEqual(firstLogFile.Name, secondLogFile.Name);
            var logLine = File.ReadAllLines(secondLogFile.FullName).Single();
            Assert.Equal("test trace", logLine);
        }

        [Fact]
        public void Flush_LogFileOpened_WritesSuccessfully()
        {
            var fileWriter = new FileWriter(_logFilePath);

            fileWriter.AppendLine("test trace");
            fileWriter.Flush();

            var directory = new DirectoryInfo(_logFilePath);
            var logFile = directory.EnumerateFiles().Single();

            // open the log file with ReadWrite to make sure logging
            // is still possible
            var logFileStream = logFile.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            fileWriter.AppendLine("test trace 2");
            fileWriter.Flush();

            logFileStream.Close();

            // verify that both lines were written successfully
            var fileLines = File.ReadAllLines(logFile.FullName);
            Assert.Equal(2, fileLines.Length);
            Assert.Collection(fileLines,
                t => Assert.EndsWith("test trace", t),
                t => Assert.EndsWith("test trace 2", t));
        }

        private void WriteLogs(string logFilePath, int numLogs)
        {
            FileWriter fileWriter = new FileWriter(logFilePath);

            for (int i = 0; i < numLogs; i++)
            {
                fileWriter.AppendLine($"Test message {Thread.CurrentThread.ManagedThreadId} {i}");
            }

            fileWriter.Flush();
        }
    }
}
