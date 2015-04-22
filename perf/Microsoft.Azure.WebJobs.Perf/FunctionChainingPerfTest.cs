// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

// This test covers the following performance metrics
// - Startup time for host (from the moment RunAndBlock is called, to the moment when the first function is invoked)
// - Execution time for a number of functions that are triggerd by queue messages

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Azure.WebJobs.Host.TestCommon.AzureSdk;
using Microsoft.VisualStudio.Diagnostics.Measurement;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Perf
{
    public static partial class FunctionChainingPerfTest
    {
        private const string HostStartMetric = "HostStart";
        private const string QueueFunctionChainMetric = "QueueChain";

        private const string PerfQueuePrefix = "%rnd%perfqueue";
        private const string FirstQueueName = PerfQueuePrefix + "start";
        private const string LastQueueName = PerfQueuePrefix + "final";

        public static CancellationTokenSource _tokenSource;

        /// <summary>
        /// Measures the time from the moment when the host is created t
        /// to the moment when the first function is invoked
        /// </summary>
        private static MeasurementBlock _startBlock;

        /// <summary>
        /// Measures the execution time of a chain of functions
        /// that pass queue messages (functions code is generated)
        /// </summary>
        private static MeasurementBlock _functionsExecutionBlock;

        private static RandomNameResolver _nameResolver = new RandomNameResolver();

        public static void Run(string connectionString)
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);

            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
            CreateTestQueues(queueClient);

            try
            {
                CloudQueue firstQueue = queueClient.GetQueueReference(_nameResolver.ResolveInString(FunctionChainingPerfTest.FirstQueueName));
                firstQueue.AddMessage(new CloudQueueMessage("Test"));

                _startBlock = MeasurementBlock.BeginNew(0, HostStartMetric);

                JobHostConfiguration hostConfig = new JobHostConfiguration(connectionString);
                hostConfig.NameResolver = _nameResolver;
                hostConfig.TypeLocator = new FakeTypeLocator(typeof(FunctionChainingPerfTest));

                JobHost host = new JobHost(hostConfig);
                _tokenSource = new CancellationTokenSource();
                Task stopTask = null;
                _tokenSource.Token.Register(() => stopTask = host.StopAsync());
                host.RunAndBlock();
                stopTask.GetAwaiter().GetResult();
            }
            finally
            {
                DeleteTestQueues(queueClient);
            }
        }

        public static void QueuePerfJobStart([QueueTrigger(FirstQueueName)] string input, [Queue(PerfQueuePrefix + "1")] out string output)
        {
            // When we reach here, it means that the host started and the first function is invoked
            // so we can stop the timer
            _startBlock.Dispose();

            output = input;

            _functionsExecutionBlock = MeasurementBlock.BeginNew(0, QueueFunctionChainMetric);
        }

        public static void QueuePerfJobEnd([QueueTrigger(LastQueueName)] string input)
        {
            // When we reach here, it means that all functions have completed and we can stop the timer
            _functionsExecutionBlock.Dispose();
            Console.WriteLine(_functionsExecutionBlock.Elapsed);

            _tokenSource.Cancel();
        }

        private static void CreateTestQueues(CloudQueueClient queueClient)
        {
            queueClient.CreateQueueOrClearIfExists(ResolveName(FunctionChainingPerfTest.FirstQueueName));
            queueClient.CreateQueueOrClearIfExists(ResolveName(FunctionChainingPerfTest.LastQueueName));

            // Create the queues to pass messages around
            for (int i = 1; i <= FunctionChainingPerfTest.NumberOfGeneratedMethods; i++)
            {
                queueClient.CreateQueueOrClearIfExists(ResolveName(FunctionChainingPerfTest.PerfQueuePrefix + i));
            }
        }

        private static void DeleteTestQueues(CloudQueueClient queueClient)
        {
             queueClient.DeleteQueueIfExists(ResolveName(FirstQueueName));
             queueClient.DeleteQueueIfExists(ResolveName(LastQueueName));

            // Create the queues to pass messages around
            for (int i = 1; i <= FunctionChainingPerfTest.NumberOfGeneratedMethods; i++)
            {
                queueClient.DeleteQueueIfExists(ResolveName(FunctionChainingPerfTest.PerfQueuePrefix + i));
            }
        }

        private static string ResolveName(string name)
        {
            return _nameResolver.ResolveInString(name);
        }
    }
}
