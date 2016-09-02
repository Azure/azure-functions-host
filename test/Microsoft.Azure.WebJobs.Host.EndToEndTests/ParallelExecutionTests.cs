// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.EndToEndTests
{
    public class ParallelExecutionTests : IDisposable
    {
        private const string TestArtifactPrefix = "e2etestparallelqueue";
        private const string TestQueueName = TestArtifactPrefix + "-%rnd%";

        private static readonly object _lock = new object();

        private static int _numberOfQueueMessages;
        private static int _receivedMessages;

        private static int _currentSimultaneouslyRunningFunctions;
        private static int _maxSimultaneouslyRunningFunctions;

        private static ManualResetEvent _allMessagesProcessed;
        private CloudQueueClient _queueClient;

        public static void ParallelQueueTrigger([QueueTrigger(TestQueueName)] int sleepTimeInSeconds)
        {
            lock (_lock)
            {
                _receivedMessages++;
                _currentSimultaneouslyRunningFunctions++;
                if (_currentSimultaneouslyRunningFunctions > _maxSimultaneouslyRunningFunctions)
                {
                    _maxSimultaneouslyRunningFunctions = _currentSimultaneouslyRunningFunctions;
                }
            }

            Thread.Sleep(sleepTimeInSeconds * 1000);

            lock (_lock)
            {
                _currentSimultaneouslyRunningFunctions--;
                if (_receivedMessages == _numberOfQueueMessages)
                {
                    _allMessagesProcessed.Set();
                }
            }
        }

        [Theory]
        // One is special case (the old behaviour)
        [InlineData(1, 1)]
        // Odd and even values
        [InlineData(2, 3)]
        [InlineData(3, 3)]
        public void MaxDegreeOfParallelism_Queues(int batchSize, int maxExpectedParallelism)
        {
            _receivedMessages = 0;
            _currentSimultaneouslyRunningFunctions = 0;
            _maxSimultaneouslyRunningFunctions = 0;
            _numberOfQueueMessages = batchSize * 3;

            RandomNameResolver nameResolver = new RandomNameResolver();
            JobHostConfiguration hostConfiguration = new JobHostConfiguration()
            {
                NameResolver = nameResolver,
                TypeLocator = new FakeTypeLocator(typeof(ParallelExecutionTests)),
            };
            hostConfiguration.Queues.BatchSize = batchSize;

            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(hostConfiguration.StorageConnectionString);
            _queueClient = storageAccount.CreateCloudQueueClient();
            CloudQueue queue = _queueClient.GetQueueReference(nameResolver.ResolveInString(TestQueueName));

            queue.CreateIfNotExists();

            for (int i = 0; i < _numberOfQueueMessages; i++)
            {
                int sleepTimeInSeconds = i % 2 == 0 ? 5 : 1;
                queue.AddMessage(new CloudQueueMessage(sleepTimeInSeconds.ToString()));
            }

            using (_allMessagesProcessed = new ManualResetEvent(initialState: false))
            using (JobHost host = new JobHost(hostConfiguration))
            {
                host.Start();
                _allMessagesProcessed.WaitOne(TimeSpan.FromSeconds(90));
                host.Stop();
            }

            Assert.Equal(_numberOfQueueMessages, _receivedMessages);
            Assert.Equal(0, _currentSimultaneouslyRunningFunctions);

            // the actual value will vary sometimes based on the speed of the machine
            // running the test.
            int delta = _maxSimultaneouslyRunningFunctions - maxExpectedParallelism;
            Assert.True(delta == 0 || delta == 1);
        }

        public void Dispose()
        {
            if (_queueClient != null)
            {
                foreach (var testQueue in _queueClient.ListQueues(TestArtifactPrefix))
                {
                    testQueue.Delete();
                }
            }
        }
    }
}