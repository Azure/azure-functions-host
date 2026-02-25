// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.AppCapabilities
{
    public class DefaultAppCapabilitiesStoreTests
    {
        [Fact]
        public async Task SetAll_WithConcurrentWrites_AllCapabilitiesAreStored()
        {
            var changeTokenSource = new TestChangeTokenSource<AppCapabilitiesOptions>();
            var store = new DefaultAppCapabilitiesStore(changeTokenSource);

            const int threadCount = 10;
            const int capabilitiesPerThread = 100;

            var tasks = new List<Task>();
            for (int i = 0; i < threadCount; i++)
            {
                int threadId = i;
                tasks.Add(Task.Run(() =>
                {
                    var capabilities = new List<KeyValuePair<string, string>>();
                    for (int j = 0; j < capabilitiesPerThread; j++)
                    {
                        capabilities.Add(new KeyValuePair<string, string>($"Thread{threadId}_Key{j}", $"Value{j}"));
                    }
                    store.SetAll(capabilities);
                }));
            }

            await Task.WhenAll(tasks);

            Assert.Equal(threadCount * capabilitiesPerThread, store.Capabilities.Count);

            for (int i = 0; i < threadCount; i++)
            {
                for (int j = 0; j < capabilitiesPerThread; j++)
                {
                    string key = $"Thread{i}_Key{j}";
                    Assert.True(store.Capabilities.ContainsKey(key), $"Missing key: {key}");
                    Assert.Equal($"Value{j}", store.Capabilities[key]);
                }
            }
        }

        [Fact]
        public async Task SetAll_WithConcurrentWritesToSameKeys_LastWriteWins()
        {
            var changeTokenSource = new TestChangeTokenSource<AppCapabilitiesOptions>();
            var store = new DefaultAppCapabilitiesStore(changeTokenSource);

            const int threadCount = 50;
            const int sharedKeyCount = 10;
            int completedThreads = 0;

            var tasks = new List<Task>();
            for (int i = 0; i < threadCount; i++)
            {
                int threadId = i;
                tasks.Add(Task.Run(() =>
                {
                    var capabilities = new List<KeyValuePair<string, string>>();
                    for (int j = 0; j < sharedKeyCount; j++)
                    {
                        capabilities.Add(new KeyValuePair<string, string>($"SharedKey{j}", $"Thread{threadId}"));
                    }
                    store.SetAll(capabilities);
                    Interlocked.Increment(ref completedThreads);
                }));
            }

            await Task.WhenAll(tasks);

            Assert.Equal(threadCount, completedThreads);
            Assert.Equal(sharedKeyCount, store.Capabilities.Count);

            for (int j = 0; j < sharedKeyCount; j++)
            {
                string key = $"SharedKey{j}";
                Assert.True(store.Capabilities.ContainsKey(key));
                Assert.Matches(@"Thread\d+", store.Capabilities[key]);
            }
        }

        [Fact]
        public async Task SetAll_WithConcurrentReadsAndWrites_ReadsDoNotThrow()
        {
            var changeTokenSource = new TestChangeTokenSource<AppCapabilitiesOptions>();
            var store = new DefaultAppCapabilitiesStore(changeTokenSource);

            const int writerCount = 5;
            const int readerCount = 10;
            const int operationsPerThread = 100;

            var allTasks = new List<Task>();

            for (int i = 0; i < writerCount; i++)
            {
                int writerId = i;
                allTasks.Add(Task.Run(() =>
                {
                    for (int j = 0; j < operationsPerThread; j++)
                    {
                        var capabilities = new List<KeyValuePair<string, string>>
                        {
                            new KeyValuePair<string, string>($"Writer{writerId}_Key{j}", $"Value{j}")
                        };
                        store.SetAll(capabilities);
                    }
                }));
            }

            for (int i = 0; i < readerCount; i++)
            {
                allTasks.Add(Task.Run(() =>
                {
                    for (int j = 0; j < operationsPerThread; j++)
                    {
                        var snapshot = store.Capabilities;
                        var count = snapshot.Count;
                        var keys = snapshot.Keys.ToList();
                        var values = snapshot.Values.ToList();

                        Assert.True(count >= 0);
                        Assert.Equal(keys.Count, count);
                        Assert.Equal(values.Count, count);
                    }
                }));
            }

            await Task.WhenAll(allTasks);

            Assert.True(store.Capabilities.Count > 0);
        }

        [Fact]
        public void SetAll_WithNullOrEmptyKeysOrValues_IgnoresInvalidEntries()
        {
            var changeTokenSource = new TestChangeTokenSource<AppCapabilitiesOptions>();
            var store = new DefaultAppCapabilitiesStore(changeTokenSource);

            var capabilities = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("ValidKey1", "ValidValue1"),
                new KeyValuePair<string, string>(null, "Value"),
                new KeyValuePair<string, string>("", "Value"),
                new KeyValuePair<string, string>("Key", null),
                new KeyValuePair<string, string>("Key2", ""),
                new KeyValuePair<string, string>("ValidKey2", "ValidValue2")
            };

            store.SetAll(capabilities);

            Assert.Equal(2, store.Capabilities.Count);
            Assert.Equal("ValidValue1", store.Capabilities["ValidKey1"]);
            Assert.Equal("ValidValue2", store.Capabilities["ValidKey2"]);
        }

        [Fact]
        public void SetAll_WithAppCapabilitiesChangeTokenSource_TriggersChange()
        {
            var changeTokenSource = new AppCapabilitiesChangeTokenSource();
            var store = new DefaultAppCapabilitiesStore(changeTokenSource);

            bool changeDetected = false;
            var changeToken = changeTokenSource.GetChangeToken();
            changeToken.RegisterChangeCallback(_ => changeDetected = true, null);

            var capabilities = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Key1", "Value1")
            };

            store.SetAll(capabilities);

            Assert.True(changeDetected);
        }

        [Fact]
        public void Capabilities_ReturnsCaseInsensitiveDictionary()
        {
            var changeTokenSource = new TestChangeTokenSource<AppCapabilitiesOptions>();
            var store = new DefaultAppCapabilitiesStore(changeTokenSource);

            var capabilities = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("TestKey", "Value1")
            };

            store.SetAll(capabilities);

            Assert.True(store.Capabilities.ContainsKey("TestKey"));
            Assert.True(store.Capabilities.ContainsKey("testkey"));
            Assert.True(store.Capabilities.ContainsKey("TESTKEY"));
        }
    }
}