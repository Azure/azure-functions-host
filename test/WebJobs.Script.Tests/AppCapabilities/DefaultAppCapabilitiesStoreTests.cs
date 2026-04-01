// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.AppCapabilities;

public class DefaultAppCapabilitiesStoreTests
{
    [Fact]
    public async Task SetAll_WithConcurrentWrites_OnlyFirstWriterWins()
    {
        var changeTokenSource = new TestChangeTokenSource<AppCapabilitiesOptions>();
        var store = new DefaultAppCapabilitiesStore(changeTokenSource);

        const int threadCount = 50;
        const int capabilitiesPerThread = 100;
        var winnerThreadId = -1;

        var tasks = new List<Task>();
        for (int i = 0; i < threadCount; i++)
        {
            int threadId = i;
            tasks.Add(Task.Run(() =>
            {
                var capabilities = new List<KeyValuePair<string, string>>();
                for (int j = 0; j < capabilitiesPerThread; j++)
                {
                    capabilities.Add(new KeyValuePair<string, string>($"Thread{threadId}_Key{j}", $"Thread{threadId}_Value{j}"));
                }

                bool wasSet = store.TrySetAll(capabilities);
                if (wasSet)
                {
                    Interlocked.CompareExchange(ref winnerThreadId, threadId, -1);
                }
            }));
        }

        await Task.WhenAll(tasks);

        Assert.NotEqual(-1, winnerThreadId);
        Assert.Equal(capabilitiesPerThread, store.Capabilities.Count);

        for (int j = 0; j < capabilitiesPerThread; j++)
        {
            string expectedKey = $"Thread{winnerThreadId}_Key{j}";
            string expectedValue = $"Thread{winnerThreadId}_Value{j}";

            Assert.True(store.Capabilities.ContainsKey(expectedKey), $"Missing key: {expectedKey}");
            Assert.Equal(expectedValue, store.Capabilities[expectedKey]);
        }

        for (int i = 0; i < threadCount; i++)
        {
            if (i == winnerThreadId)
            {
                continue;
            }

            for (int j = 0; j < capabilitiesPerThread; j++)
            {
                string loserKey = $"Thread{i}_Key{j}";
                Assert.False(store.Capabilities.ContainsKey(loserKey), $"Loser thread {i} should not have keys in store");
            }
        }
    }

    [Fact]
    public void SetAll_AfterStoreIsPopulated_ReturnsFalseAndDoesNotModify()
    {
        var changeTokenSource = new TestChangeTokenSource<AppCapabilitiesOptions>();
        var store = new DefaultAppCapabilitiesStore(changeTokenSource);

        var firstCapabilities = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("Key1", "FirstValue1"),
            new KeyValuePair<string, string>("Key2", "FirstValue2")
        };

        bool firstResult = store.TrySetAll(firstCapabilities);
        Assert.True(firstResult);
        Assert.Equal(2, store.Capabilities.Count);

        var secondCapabilities = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("Key3", "SecondValue3"),
            new KeyValuePair<string, string>("Key4", "SecondValue4")
        };

        bool secondResult = store.TrySetAll(secondCapabilities);
        Assert.False(secondResult);
        Assert.Equal(2, store.Capabilities.Count);
        Assert.Equal("FirstValue1", store.Capabilities["Key1"]);
        Assert.Equal("FirstValue2", store.Capabilities["Key2"]);
        Assert.False(store.Capabilities.ContainsKey("Key3"));
        Assert.False(store.Capabilities.ContainsKey("Key4"));
    }

    [Fact]
    public void Clear_ThenSetAll_AllowsNewValuesToBeSet()
    {
        var changeTokenSource = new TestChangeTokenSource<AppCapabilitiesOptions>();
        var store = new DefaultAppCapabilitiesStore(changeTokenSource);

        var firstCapabilities = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("Key1", "Value1")
        };

        store.TrySetAll(firstCapabilities);
        Assert.Single(store.Capabilities);

        store.Clear();
        Assert.Throws<InvalidOperationException>(() => store.Capabilities);

        var secondCapabilities = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("Key2", "Value2")
        };

        bool result = store.TrySetAll(secondCapabilities);
        Assert.True(result);
        Assert.Single(store.Capabilities);
        Assert.Equal("Value2", store.Capabilities["Key2"]);
        Assert.False(store.Capabilities.ContainsKey("Key1"));
    }

    [Fact]
    public async Task ClearAndSetAll_WithConcurrentOperations_DoNotInterweave()
    {
        var changeTokenSource = new TestChangeTokenSource<AppCapabilitiesOptions>();
        var store = new DefaultAppCapabilitiesStore(changeTokenSource);

        const int iterations = 100;
        const int keysPerSet = 10;

        var tasks = new List<Task>();

        for (int i = 0; i < iterations; i++)
        {
            int iteration = i;

            tasks.Add(Task.Run(() =>
            {
                var capabilities = new List<KeyValuePair<string, string>>();
                for (int j = 0; j < keysPerSet; j++)
                {
                    capabilities.Add(new KeyValuePair<string, string>($"Iteration{iteration}_Key{j}", $"Iteration{iteration}_Value{j}"));
                }
                store.TrySetAll(capabilities);
            }));

            if (i % 5 == 0)
            {
                tasks.Add(Task.Run(() => store.Clear()));
            }
        }

        await Task.WhenAll(tasks);

        // Handle the case where Clear() was the last operation
        try
        {
            var snapshot = store.Capabilities;
            var count = snapshot.Count;

            // Verify atomicity: either empty or contains a complete set from one iteration
            Assert.True(count is 0 or keysPerSet, $"Store should have either 0 or {keysPerSet} keys, but has {count}");

            if (count == keysPerSet)
            {
                var firstKey = snapshot.Keys.First();
                Assert.Matches(@"Iteration\d+_Key\d+", firstKey);

                var iterationMatch = System.Text.RegularExpressions.Regex.Match(firstKey, @"Iteration(\d+)_Key");
                Assert.True(iterationMatch.Success);
                string iterationId = iterationMatch.Groups[1].Value;

                foreach (var kvp in snapshot)
                {
                    Assert.StartsWith($"Iteration{iterationId}_", kvp.Key, StringComparison.Ordinal);
                    Assert.StartsWith($"Iteration{iterationId}_Value", kvp.Value, StringComparison.Ordinal);
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Valid outcome: Clear() was the last operation, leaving store uninitialized
            // This is acceptable and demonstrates proper atomicity
        }
    }

    [Fact]
    public async Task SetAll_WithConcurrentReadsAndWrites_ReadsDoNotThrowUnexpectedError()
    {
        var changeTokenSource = new TestChangeTokenSource<AppCapabilitiesOptions>();
        var store = new DefaultAppCapabilitiesStore(changeTokenSource);

        const int writerCount = 10;
        const int readerCount = 20;
        const int operationsPerReader = 1000;

        var allTasks = new List<Task>();

        for (int i = 0; i < writerCount; i++)
        {
            int writerId = i;
            allTasks.Add(Task.Run(() =>
            {
                var capabilities = new List<KeyValuePair<string, string>>();
                for (int j = 0; j < 50; j++)
                {
                    capabilities.Add(new KeyValuePair<string, string>($"Writer{writerId}_Key{j}", $"Value{j}"));
                }
                store.TrySetAll(capabilities);
            }));
        }

        for (int i = 0; i < readerCount; i++)
        {
            allTasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < operationsPerReader; j++)
                {
                    try
                    {
                        var snapshot = store.Capabilities;
                        var count = snapshot.Count;
                        var keys = snapshot.Keys.ToList();
                        var values = snapshot.Values.ToList();

                        Assert.True(count >= 0);
                        Assert.Equal(keys.Count, count);
                        Assert.Equal(values.Count, count);
                    }
                    catch (InvalidOperationException)
                    {
                        // Valid outcome: store not yet initialized by any writer
                        // The test verifies reads don't throw unexpected exceptions during concurrent writes
                    }
                }
            }));
        }

        await Task.WhenAll(allTasks);
    }

    [Fact]
    public void SetAll_WithNullOrEmptyKeysOrValues_IgnoresInvalidEntries()
    {
        var changeTokenSource = new TestChangeTokenSource<AppCapabilitiesOptions>();
        var store = new DefaultAppCapabilitiesStore(changeTokenSource);

        var capabilities = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("ValidKey1", "ValidValue1"),
            new KeyValuePair<string, string>(string.Empty, "Value"),
            new KeyValuePair<string, string>("Key", null),
            new KeyValuePair<string, string>("Key2", string.Empty),
            new KeyValuePair<string, string>("ValidKey2", "ValidValue2")
        };

        store.TrySetAll(capabilities);

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

        store.TrySetAll(capabilities);

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

        store.TrySetAll(capabilities);

        Assert.True(store.Capabilities.ContainsKey("TestKey"));
        Assert.True(store.Capabilities.ContainsKey("testkey"));
        Assert.True(store.Capabilities.ContainsKey("TESTKEY"));
    }

    [Fact]
    public void Clear_TriggersChangeNotification()
    {
        var changeTokenSource = new AppCapabilitiesChangeTokenSource();
        var store = new DefaultAppCapabilitiesStore(changeTokenSource);

        var changeToken = changeTokenSource.GetChangeToken();
        var hasChangedBefore = changeToken.HasChanged;

        store.Clear();

        Assert.False(hasChangedBefore);
        Assert.True(changeToken.HasChanged);
    }

    [Fact]
    public void SetAll_WithEmptyCapabilities_ReturnsTrueAndSetsEmptyStore()
    {
        var changeTokenSource = new TestChangeTokenSource<AppCapabilitiesOptions>();
        var store = new DefaultAppCapabilitiesStore(changeTokenSource);

        var capabilities = new List<KeyValuePair<string, string>>();

        bool result = store.TrySetAll(capabilities);

        Assert.True(result);
        Assert.Empty(store.Capabilities);

        var secondCapabilities = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("Key1", "Value1")
        };

        bool secondResult = store.TrySetAll(secondCapabilities);
        Assert.False(secondResult);
        Assert.Empty(store.Capabilities);
    }

    [Fact]
    public void Capabilities_BeforeInitialization_ThrowsInvalidOperationException()
    {
        var changeTokenSource = new TestChangeTokenSource<AppCapabilitiesOptions>();
        var store = new DefaultAppCapabilitiesStore(changeTokenSource);

        var exception = Assert.Throws<InvalidOperationException>(() => store.Capabilities);

        Assert.Equal("Capabilities have not been initialized.", exception.Message);
    }

    [Fact]
    public void Capabilities_AfterClear_ThrowsInvalidOperationException()
    {
        var changeTokenSource = new TestChangeTokenSource<AppCapabilitiesOptions>();
        var store = new DefaultAppCapabilitiesStore(changeTokenSource);

        var capabilities = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("Key1", "Value1")
        };

        store.TrySetAll(capabilities);
        Assert.Single(store.Capabilities);

        store.Clear();

        var exception = Assert.Throws<InvalidOperationException>(() => store.Capabilities);

        Assert.Equal("Capabilities have not been initialized.", exception.Message);
    }
}
