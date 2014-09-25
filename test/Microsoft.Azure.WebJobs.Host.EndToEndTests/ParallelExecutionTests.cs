// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Xunit;
using Xunit.Extensions;

namespace Microsoft.Azure.WebJobs.Host.EndToEndTests
{
    public class ParallelExecutionTests
    {
        private const string TestQueueName = "parallelqueue-%rnd%";

        private static readonly object _lock = new object();

        private static int _numberOfQueueMessages;
        private static int _receivedMessages;

        private static int _currentSimultaneouslyRunningFunctions;
        private static int _maxSimultaneouslyRunningFunctions;

        private static ManualResetEvent _allMessagesProcessed;

        public static void ParallelQueueTrigger([QueueTrigger(TestQueueName)] int sleepTimeInSeconds)
        {
            lock(_lock)
            {
                _receivedMessages++;
                _currentSimultaneouslyRunningFunctions++;
                if (_currentSimultaneouslyRunningFunctions > _maxSimultaneouslyRunningFunctions)
                {
                    _maxSimultaneouslyRunningFunctions = _currentSimultaneouslyRunningFunctions;
                }
            }

            Thread.Sleep(sleepTimeInSeconds * 1000);

            lock(_lock)
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
        [InlineData(1)]
        // Odd and even values
        [InlineData(2)]
        [InlineData(3)]
        public void MaxDegreeOfParallelism_Queues(int batchSize)
        {
            _receivedMessages = 0;
            _currentSimultaneouslyRunningFunctions = 0;
            _maxSimultaneouslyRunningFunctions = 0;

            int expectedMaxSimultaneouslyRunningFunctions = (int)Math.Floor(batchSize + 0.5 * batchSize);
            _numberOfQueueMessages = batchSize * 3;

            RandomNameResolver nameResolver = new RandomNameResolver();
            JobHostConfiguration hostConfiguration = new JobHostConfiguration()
            {
                NameResolver = nameResolver,
                TypeLocator = new FakeTypeLocator(typeof(ParallelExecutionTests)),
            };
            hostConfiguration.Queues.BatchSize = batchSize;

            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(hostConfiguration.StorageConnectionString);
            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
            CloudQueue queue = queueClient.GetQueueReference(nameResolver.ResolveInString(TestQueueName));

            queue.CreateIfNotExists();

            try
            {
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
                Assert.Equal(expectedMaxSimultaneouslyRunningFunctions , _maxSimultaneouslyRunningFunctions);
            }
            finally
            {
                queue.DeleteIfExists();
            }
        }
    }
}
