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
            _logFilePath = Path.Combine(Path.GetTempPath(), "WebJobs.Script.Tests", "FileTraceWriterTests");

            if (Directory.Exists(_logFilePath))
            {
                Directory.Delete(_logFilePath, recursive: true);
            }
        }

        [Fact]
        public async Task MultipleConcurrentInstances_WritesAreSerialized()
        {
            int numLines = 100;

            // first ensure the file exists by writing a single entry
            // this ensures all the instances below will be operating on
            // the same file
            WriteLogs(_logFilePath, 1);

            // start 3 concurrent instances
            await Task.WhenAll(
                Task.Run(() => WriteLogs(_logFilePath, numLines)),
                Task.Run(() => WriteLogs(_logFilePath, numLines)),
                Task.Run(() => WriteLogs(_logFilePath, numLines)));

            string logFile = Directory.EnumerateFiles(_logFilePath).Single();
            string[] fileLines = File.ReadAllLines(logFile);
            Assert.Equal((3 * numLines) + 1, fileLines.Length);
        }

        [Fact]
        public async Task SetNewLogFile_PurgesOldLogFiles()
        {
            DirectoryInfo directory = new DirectoryInfo(_logFilePath);
            directory.Create();

            // below test expects the retention days to be set to 1
            Assert.Equal(1, FileWriter.LastModifiedCutoffDays);

            // create some log files
            List<FileInfo> logFiles = new List<FileInfo>();
            int initialCount = 5;
            for (int i = 0; i < initialCount; i++)
            {
                string fileName = string.Format("{0}-{1}.log", i, FileWriter.GetInstanceId());
                string path = Path.Combine(_logFilePath, fileName);
                Thread.Sleep(50);
                File.WriteAllText(path, "Test Logs");
                logFiles.Add(new FileInfo(path));
            }

            // mark some of the files as old - we expect
            // all of these to be purged
            File.SetLastWriteTime(logFiles[2].FullName, DateTime.Now.Subtract(TimeSpan.FromDays(1)));
            File.SetLastWriteTime(logFiles[1].FullName, DateTime.Now.Subtract(TimeSpan.FromDays(1)));
            File.SetLastWriteTime(logFiles[0].FullName, DateTime.Now.Subtract(TimeSpan.FromDays(2)));

            await Task.Delay(2000);

            var files = directory.GetFiles().OrderByDescending(p => p.LastWriteTime).ToArray();
            Assert.Equal(initialCount, files.Length);

            FileWriter fileWriter = new FileWriter(_logFilePath);
            fileWriter.SetNewLogFile();

            files = directory.GetFiles().OrderByDescending(p => p.LastWriteTime).ToArray();

            await TestHelpers.Await(() =>
            {
                files = directory.GetFiles().OrderByDescending(p => p.LastWriteTime).ToArray();
                return files.Length == 2;
            }, timeout: 2000);

            // verify the correct log files were purged and the 2
            // most recent files were retained
            Assert.True(files[0].Name.StartsWith("4"));
            Assert.True(files[1].Name.StartsWith("3"));
        }

        [Fact]
        public void SetNewLogFile_EmptyDirectory_Succeeds()
        {
            DirectoryInfo directory = new DirectoryInfo(_logFilePath);
            directory.Create();

            FileWriter fileWriter = new FileWriter(_logFilePath);
            fileWriter.SetNewLogFile();

            var files = directory.GetFiles().OrderByDescending(p => p.LastWriteTime).ToArray();
            Assert.Equal(0, files.Length);

            fileWriter.AppendLine("Test log");
            fileWriter.Flush();

            files = directory.GetFiles().OrderByDescending(p => p.LastWriteTime).ToArray();
            Assert.Equal(1, files.Length);
        }

        [Fact]
        public void Trace_ReusesLastFile()
        {
            DirectoryInfo directory = new DirectoryInfo(_logFilePath);
            directory.Create();

            int count = directory.EnumerateFiles().Count();
            Assert.Equal(0, count);

            for (int i = 0; i < 3; i++)
            {
                FileWriter fileWriter = new FileWriter(_logFilePath);
                fileWriter.AppendLine("Testing");
                fileWriter.Flush();
            }

            count = directory.EnumerateFiles().Count();
            Assert.Equal(1, count);

            string logFile = directory.EnumerateFiles().First().FullName;
            string[] fileLines = File.ReadAllLines(logFile);
            Assert.Equal(3, fileLines.Length);
        }

        [Fact]
        public async Task Trace_ThrottlesLogs()
        {
            DirectoryInfo directory = new DirectoryInfo(_logFilePath);
            directory.Create();

            int numLogs = 10000;
            int numIterations = 3;
            for (int i = 0; i < numIterations; i++)
            {
                Task ignore = Task.Run(() => WriteLogs(_logFilePath, numLogs));
                await Task.Delay(1000);
            }

            string logFile = directory.EnumerateFiles().First().FullName;
            string[] fileLines = File.ReadAllLines(logFile);
            Assert.True(fileLines.Length == ((FileWriter.MaxLogLinesPerFlushInterval * numIterations) + 3));
            Assert.Equal(3, fileLines.Count(p => p.Contains("Log output threshold exceeded.")));
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
