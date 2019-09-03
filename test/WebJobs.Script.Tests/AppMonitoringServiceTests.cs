// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Logging;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class AppMonitoringServiceTests
    {
        [Theory]
        [InlineData("app_offline.htm", "add", 0, 1)]
        [InlineData("app_offline.htm", "delete", 1, 0)]
        [InlineData("host.json", "add", 0, 0)]
        [InlineData("host.json", "delete", 0, 0)]
        public async Task TestRestartEvent_WhenFilesUpdate(string fileName, string changeType, int expectRestartCount, int expectShutdownCount)
        {
            using (var directory = new TempDirectory())
            {
                // Setup
                string tempDir = directory.Path;
                Directory.CreateDirectory(Path.Combine(tempDir, "Host"));

                if (changeType == "add")
                {
                    File.Create(Path.Combine(tempDir, fileName));
                }

                var appOptions = new ScriptApplicationHostOptions
                {
                    LogPath = tempDir,
                    ScriptPath = tempDir
                };
                var loggerFactory = new LoggerFactory();
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
                AppMonitoringService appMonitoringService = new AppMonitoringService(new TestOptionsMonitor<ScriptApplicationHostOptions>(appOptions),
                    loggerFactory, eventManager);

                await appMonitoringService.StartAsync(new CancellationToken(canceled: false));

                var watcherType = changeType == "add" ? WatcherChangeTypes.Created : WatcherChangeTypes.Deleted;
                var offlineEventArgs = new FileSystemEventArgs(watcherType, tempDir, fileName);
                FileEvent offlinefileEvent = new FileEvent("ScriptFiles", offlineEventArgs);

                eventManager.Publish(offlinefileEvent);

                // Test
                Assert.Equal(expectShutdownCount, shutdownCount);
                Assert.Equal(expectRestartCount, restartCount);
            }
        }
    }
}
