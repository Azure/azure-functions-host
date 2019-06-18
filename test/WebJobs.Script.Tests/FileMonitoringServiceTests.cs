// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FileMonitoringServiceTests
    {
        [Theory]
        [InlineData(@"C:\Functions\Scripts\Shared\Test.csx", "Shared")]
        [InlineData(@"C:\Functions\Scripts\Shared\Sub1\Sub2\Test.csx", "Shared")]
        [InlineData(@"C:\Functions\Scripts\Shared", "Shared")]
        public static void GetRelativeDirectory_ReturnsExpectedDirectoryName(string path, string expected)
        {
            Assert.Equal(expected, FileMonitoringService.GetRelativeDirectory(path, @"C:\Functions\Scripts"));
        }

        [Theory]
        [InlineData("app_offline.htm", 150, true, false)]
        [InlineData("app_offline.htm", 10, true, false)]
        [InlineData("host.json", 0, false, false)]
        [InlineData("host.json", 200, false, false)]
        [InlineData("host.json", 1000, false, true)]
        public static async Task TestAppOfflineDebounceTime(string fileName, int delayInMs, bool expectShutdown, bool expectRestart)
        {
            using (var directory = new TempDirectory())
            {
                // Setup
                string tempDir = directory.Path;
                Directory.CreateDirectory(Path.Combine(tempDir, "Host"));
                File.Create(Path.Combine(tempDir, fileName));

                var jobHostOptions = new ScriptJobHostOptions
                {
                    RootLogPath = tempDir,
                    RootScriptPath = tempDir,
                    FileWatchingEnabled = true
                };
                var loggerFactory = new LoggerFactory();
                var mockWebHostEnvironment = new Mock<IScriptJobHostEnvironment>(MockBehavior.Loose);
                var mockEventManager = new ScriptEventManager();

                // Act
                FileMonitoringService fileMonitoringService = new FileMonitoringService(new OptionsWrapper<ScriptJobHostOptions>(jobHostOptions),
                    loggerFactory, mockEventManager, mockWebHostEnvironment.Object);
                await fileMonitoringService.StartAsync(new CancellationToken(canceled: false));

                var offlineEventArgs = new FileSystemEventArgs(WatcherChangeTypes.Created, tempDir, fileName);
                FileEvent offlinefileEvent = new FileEvent("ScriptFiles", offlineEventArgs);

                var randomFileEventArgs = new FileSystemEventArgs(WatcherChangeTypes.Created, tempDir, "random.txt");
                FileEvent randomFileEvent = new FileEvent("ScriptFiles", randomFileEventArgs);

                mockEventManager.Publish(offlinefileEvent);
                await Task.Delay(delayInMs);
                mockEventManager.Publish(randomFileEvent);

                // Test
                if (expectShutdown)
                {
                    mockWebHostEnvironment.Verify(m => m.Shutdown());
                }
                else
                {
                    mockWebHostEnvironment.Verify(m => m.Shutdown(), Times.Never);
                }

                if (expectRestart)
                {
                    mockWebHostEnvironment.Verify(m => m.RestartHost());
                }
                else
                {
                    mockWebHostEnvironment.Verify(m => m.RestartHost(), Times.Never);
                }
            }
        }
    }
}
