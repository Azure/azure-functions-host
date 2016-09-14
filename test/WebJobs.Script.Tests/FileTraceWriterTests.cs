﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
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
        public async Task SetNewLogFile_PurgesOldLogFiles()
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

            await Task.Delay(2000);

            var files = directory.GetFiles().OrderByDescending(p => p.LastWriteTime).ToArray();
            Assert.Equal(initialCount, files.Length);

            FileTraceWriter traceWriter = new FileTraceWriter(_logFilePath, TraceLevel.Verbose);
            traceWriter.SetNewLogFile();

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

            FileTraceWriter traceWriter = new FileTraceWriter(_logFilePath, TraceLevel.Verbose);
            traceWriter.SetNewLogFile();

            var files = directory.GetFiles().OrderByDescending(p => p.LastWriteTime).ToArray();
            Assert.Equal(0, files.Length);

            traceWriter.Verbose("Test log");
            traceWriter.Flush();

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
                FileTraceWriter traceWriter = new FileTraceWriter(_logFilePath, TraceLevel.Verbose);
                traceWriter.Verbose("Testing");
                traceWriter.Flush();
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
            Assert.True(fileLines.Length == ((FileTraceWriter.MaxLogLinesPerFlushInterval * numIterations) + 3));
            Assert.Equal(3, fileLines.Count(p => p.Contains("Log output threshold exceeded.")));
        }

        [Fact]
        public void Trace_WritesExpectedLogs()
        {
            DirectoryInfo directory = new DirectoryInfo(_logFilePath);
            directory.Create();

            int count = directory.EnumerateFiles().Count();
            Assert.Equal(0, count);

            FileTraceWriter traceWriter = new FileTraceWriter(_logFilePath, TraceLevel.Info);

            traceWriter.Verbose("Test Verbose");
            traceWriter.Info("Test Info");
            traceWriter.Warning("Test Warning");
            traceWriter.Error("Test Error");

            // trace a system event - expect it to be ignored
            var properties = new Dictionary<string, object>
            {
                { ScriptConstants.TracePropertyIsSystemTraceKey, true }
            };
            traceWriter.Info("Test System", properties);

            traceWriter.Flush();

            string logFile = directory.EnumerateFiles().First().FullName;
            string text = File.ReadAllText(logFile);
            Assert.True(text.Contains("Test Error"));
            Assert.True(text.Contains("Test Warning"));
            Assert.True(text.Contains("Test Info"));
            Assert.False(text.Contains("Test Verbose"));
            Assert.False(text.Contains("Test System"));
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
