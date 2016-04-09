// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Xunit;

namespace WebJobs.Script.Tests
{
    public class FileTraceWriterTests
    {
        private string _logFilePath;

        public FileTraceWriterTests()
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
        public void SetNewLogFile_PurgesOldLogFiles()
        {
            DirectoryInfo directory = new DirectoryInfo(_logFilePath);
            directory.Create();

            // below test expects the retention days to be set to 1
            Assert.Equal(1, FileTraceWriter.LastModifiedCutoffDays);

            // create some log files
            List<FileInfo> logFiles = new List<FileInfo>();
            int initialCount = 5;
            for (int i = 0; i < initialCount; i++)
            {
                string fileName = string.Format("{0}-{1}.log", i, FileTraceWriter.GetInstanceId());
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

            var files = directory.GetFiles().OrderByDescending(p => p.LastWriteTime).ToArray();
            Assert.Equal(initialCount, files.Length);

            FileTraceWriter traceWriter = new FileTraceWriter(_logFilePath, TraceLevel.Verbose);
            traceWriter.SetNewLogFile();

            files = directory.GetFiles().OrderByDescending(p => p.LastWriteTime).ToArray();

            // verify the correct log files were purged and the 2
            // most recent files were retained
            Assert.Equal(2, files.Length);
            Assert.True(files[0].Name.StartsWith("4"));
            Assert.True(files[1].Name.StartsWith("3"));
        }

        [Fact]
        public void SetNewLogFile_EmptyDirectory_Succeeds()
        {
            DirectoryInfo directory = new DirectoryInfo(_logFilePath);
            directory.Create();

            FileTraceWriter traceWriter = new FileTraceWriter(_logFilePath, TraceLevel.Verbose);
            traceWriter.SetNewLogFile();

            var files = directory.GetFiles().OrderByDescending(p => p.LastWriteTime).ToArray();
            Assert.Equal(0, files.Length);

            traceWriter.Verbose("Test log");
            traceWriter.Flush();

            files = directory.GetFiles().OrderByDescending(p => p.LastWriteTime).ToArray();
            Assert.Equal(1, files.Length);
        }

        private void WriteLogs(string logFilePath, int numLogs)
        {
            FileTraceWriter traceWriter = new FileTraceWriter(logFilePath, TraceLevel.Verbose);

            for (int i = 0; i < numLogs; i++)
            {
                traceWriter.Verbose(string.Format("Test message {0} {1}", Thread.CurrentThread.ManagedThreadId, i));
            }

            traceWriter.Flush();
        }
    }
}
