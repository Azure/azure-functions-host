// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Azure.WebJobs.Host.TestCommon.AzureSdk;
using Microsoft.VisualStudio.Diagnostics.Measurement;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Perf
{
    public class QueueOverheadPerfTest
    {
        private const int BatchSize = 16;
        private const string QueueLoggingOverheadMetric = "Overhead-Logging";
        private const string QueueNoLoggingOverheadMetric = "Overhead-NoLogging";

        private const string QueueName = "overhead-%rnd%";
        private const int NumberOfMessages = 50;

        private static RandomNameResolver _nameResolver = new RandomNameResolver();

        private static string _connectionString;
        private static CloudStorageAccount _storageAccount;
        private static CloudQueueClient _queueClient;

        private static int _receivedMessages;

        private static ManualResetEvent _firstMessagesReceivedEvent;
        private static ManualResetEvent _allMessagesReceivedEvent;

        public static void Run(string connectionString, bool disableLogging)
        {
            _connectionString = connectionString;
            _storageAccount = CloudStorageAccount.Parse(connectionString);
            _queueClient = _storageAccount.CreateCloudQueueClient();

            try
            {
                TimeSpan azureSDKTime = RunAzureSDKTest();
                TimeSpan webJobsSDKTime = RunWebJobsSDKTest(disableLogging);

                // Convert to ulong because the measurment block does not support other data type
                ulong perfRatio = (ulong)((webJobsSDKTime.TotalMilliseconds / azureSDKTime.TotalMilliseconds) * 100);

                Console.WriteLine("--- Results ---");
                Console.WriteLine("Azure SDK:   {0} ms: ", azureSDKTime.TotalMilliseconds);
                Console.WriteLine("WebJobs SDK: {0} ms: ", webJobsSDKTime.TotalMilliseconds);

                Console.WriteLine("Perf ratio (x100, long): {0}", perfRatio);

                MeasurementBlock.Mark(
                    perfRatio,
                    (disableLogging ? QueueNoLoggingOverheadMetric : QueueLoggingOverheadMetric) + ";Ratio;Percent");
            }
            finally
            {
                Cleanup();
            }
        }

        #region Azure SDK test

        public static TimeSpan RunAzureSDKTest()
        {
            Console.WriteLine("Running the Azure SDK test...");

            WriteTestMessages();

            TimeBlock block = new TimeBlock();

            RunAzureSDKTestInternal();

            block.End();
            return block.ElapsedTime;
        }

        private static void RunAzureSDKTestInternal()
        {
            CloudQueue queue = _queueClient.GetQueueReference(ResolveName(QueueName));

            int messagesReceived = 0;
            while (messagesReceived < NumberOfMessages)
            {
                IEnumerable<CloudQueueMessage> messages = queue.GetMessages(BatchSize);
                if (messages != null)
                {
                    Parallel.ForEach(messages, (message) => queue.DeleteMessage(message));
                    messagesReceived += messages.Count();
                }
            }
        }

        #endregion

        #region WebJobs SDK test

        private static TimeSpan RunWebJobsSDKTest(bool disableLogging)
        {
            Console.WriteLine("Running the WebJobs SDK test...");

            WriteTestMessages();

            TimeBlock block = RunWebJobsSDKTestInternal(disableLogging);
            return block.ElapsedTime;
        }

        private static TimeBlock RunWebJobsSDKTestInternal(bool disableLogging)
        {
            JobHostConfiguration hostConfig = new JobHostConfiguration(_connectionString);
            hostConfig.Queues.BatchSize = BatchSize;
            hostConfig.NameResolver = _nameResolver;
            hostConfig.TypeLocator = new SimpleTypeLocator(typeof(QueueOverheadPerfTest));

            if (disableLogging)
            {
                hostConfig.DashboardConnectionString = null;
            }

            _receivedMessages = 0;

            using (_firstMessagesReceivedEvent = new ManualResetEvent(initialState: false))
            using (_allMessagesReceivedEvent = new ManualResetEvent(initialState: false))
            using (JobHost host = new JobHost(hostConfig))
            {
                host.Start();

                _firstMessagesReceivedEvent.WaitOne();
                TimeBlock block = new TimeBlock();
                _allMessagesReceivedEvent.WaitOne();
                block.End();

                return block;
            }
        }

        public static void QueueListener([QueueTrigger(QueueName)] string message)
        {
            _firstMessagesReceivedEvent.Set();

            Interlocked.Increment(ref _receivedMessages);
            if (_receivedMessages == NumberOfMessages)
            {
                _allMessagesReceivedEvent.Set();
            }
        }

        #endregion

        private static void WriteTestMessages()
        {
            string resolvedQueueName = ResolveName(QueueName);

            _queueClient.CreateQueueOrClearIfExists(ResolveName(QueueName));
            CloudQueue queue = _queueClient.GetQueueReference(ResolveName(QueueName));
            for (int i = 0; i < NumberOfMessages; i++)
            {
                queue.AddMessage(new CloudQueueMessage("x"));
            }
        }

        private static void Cleanup()
        {
            _queueClient.DeleteQueueIfExists(ResolveName(QueueName));
        }

        private static string ResolveName(string name)
        {
            return _nameResolver.ResolveInString(name);
        }
    }
}
