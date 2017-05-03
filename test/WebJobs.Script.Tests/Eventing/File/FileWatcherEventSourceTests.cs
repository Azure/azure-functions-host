// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Eventing.File;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Eventing
{
    public class FileWatcherEventSourceTests
    {
        [Fact]
        public async Task Constructor_CreatesExpectedFileWatcher()
        {
            using (var directory = new TempDirectory())
            using (var eventManager = new ScriptEventManager())
            using (var eventSource = new FileWatcherEventSource(eventManager, "TestSource", directory.Path))
            {
                var events = new List<FileEvent>();
                eventManager.OfType<FileEvent>().Subscribe(e => events.Add(e));

                File.WriteAllText(Path.Combine(directory.Path, "test.txt"), "Test");

                await TestHelpers.Await(() => events.Count == 2 /* We expect created and changed*/, timeout: 5000, pollingInterval: 500);

                Assert.True(events.All(e => string.Equals(e.Source, "TestSource", StringComparison.Ordinal)), "Event source did not match the expected source");
                Assert.True(events.All(e => string.Equals(e.Name, "FileEvent", StringComparison.Ordinal)));
                Assert.Equal("test.txt", events.First().FileChangeArguments.Name);
                Assert.Equal(Path.Combine(directory.Path, "test.txt"), events[0].FileChangeArguments.FullPath);
                Assert.Equal(WatcherChangeTypes.Created, events[0].FileChangeArguments.ChangeType);
                Assert.Equal(WatcherChangeTypes.Changed, events[1].FileChangeArguments.ChangeType);
            }
        }

        [Fact]
        public async Task DisposedSource_DoesNotPublishEvents()
        {
            using (var directory = new TempDirectory())
            using (var eventManager = new ScriptEventManager())
            {
                var events = new List<FileEvent>();
                eventManager.OfType<FileEvent>().Subscribe(e => events.Add(e));
                using (var eventSource = new FileWatcherEventSource(eventManager, "TestSource", directory.Path))
                {
                    File.WriteAllText(Path.Combine(directory.Path, "test.txt"), "Test");
                    await TestHelpers.Await(() => events.Count == 2 /* We expect created and changed*/, timeout: 5000, pollingInterval: 500);
                }

                using (var fileStream = File.OpenWrite(Path.Combine(directory.Path, "test.txt")))
                using (var writer = new StreamWriter(fileStream))
                {
                    await writer.WriteLineAsync("Test 123");
                }

                // A small wait to ensure events would be propagated
                await Task.Delay(2000);

                Assert.Equal(2, events.Count);
            }
        }
    }
}
