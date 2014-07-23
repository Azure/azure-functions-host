// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Threading;
using Microsoft.Azure.Jobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.IntegrationTests
{
    public class CancellationTests
    {
        private const string QueueName = "test-stop-cancellation";

        [Fact]
        public void TestStopTriggersCancellationToken()
        {
            // Arrange
            CloudStorageAccount account = CloudStorageAccount.DevelopmentStorageAccount;
            CloudQueueClient client = account.CreateCloudQueueClient();
            CloudQueue queue = client.GetQueueReference(QueueName);
            queue.CreateIfNotExists();

            try
            {
                queue.AddMessage(new CloudQueueMessage("test"));

                using (JobHost host = JobHostFactory.Create<Program>(account))
                using (EventWaitHandle monitoringCancellationToken = new AutoResetEvent(initialState: false))
                {
                    Program.MonitoringCancellationToken = monitoringCancellationToken;
                    host.Start();

                    bool monitoring = monitoringCancellationToken.WaitOne(2000);
                    Assert.True(monitoring); // Guard

                    // Act
                    host.Stop();
                }

                // Assert
                Assert.True(Program.CancellationTokenTriggered);
            }
            finally
            {
                Program.MonitoringCancellationToken = null;
                queue.DeleteIfExists();
            }
        }

        public class Program
        {
            public static EventWaitHandle MonitoringCancellationToken;
            public static bool CancellationTokenTriggered;

            public static void BindCancellationToken([QueueTrigger(QueueName)] string ignore,
                CancellationToken cancellationToken)
            {
                CancellationTokenTriggered = false;
                bool set = MonitoringCancellationToken.Set();
                Assert.True(set); // Guard
                CancellationTokenTriggered = cancellationToken.WaitHandle.WaitOne(3000);
            }
        }
    }
}
