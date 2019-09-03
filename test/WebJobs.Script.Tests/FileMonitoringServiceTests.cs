// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Eventing.File;
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
        [InlineData("host.json", "add", false, true)]
        [InlineData("host.json", "delete", false, true)]
        [InlineData("app_offline.htm", "add", false, false)]
        [InlineData("app_offline.htm", "delete", false, false)]
        [InlineData("App_offline.htm", "add", false, false)]
        [InlineData("App_offline.htm", "delete", false, false)]
        public static async Task TestFileChange_RestartsHost(string fileName, string action, bool expectShutdown, bool expectRestart)
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
                var eventManager = new ScriptEventManager();

                var sourceSet = new HashSet<string>();
                int shutdownCount = 0;
                int restartCount = 0;

                eventManager.OfType<HostShutdownEvent>().Subscribe(e =>
                {
                    shutdownCount++;
                    sourceSet.Add(e.Source);
                });

                eventManager.OfType<HostRestartEvent>().Subscribe(e =>
                {
                    restartCount++;
                    sourceSet.Add(e.Source);
                });

                // Act
                FileMonitoringService fileMonitoringService = new FileMonitoringService(new OptionsWrapper<ScriptJobHostOptions>(jobHostOptions),
                    loggerFactory, eventManager);
                await fileMonitoringService.StartAsync(new CancellationToken(canceled: false));

                var changeType = action == "add" ? WatcherChangeTypes.Created : WatcherChangeTypes.Deleted;
                var randomFileEventArgs = new FileSystemEventArgs(changeType, tempDir, fileName);
                FileEvent randomFileEvent = new FileEvent("ScriptFiles", randomFileEventArgs);

                eventManager.Publish(randomFileEvent);

                // Test
                if (expectShutdown)
                {
                    Assert.Equal(1, shutdownCount);
                }
                else
                {
                    Assert.Equal(0, shutdownCount);
                }

                if (expectRestart)
                {
                    Assert.Equal(1, restartCount);
                }
                else
                {
                    Assert.Equal(0, restartCount);
                }
            }
        }
    }
}
