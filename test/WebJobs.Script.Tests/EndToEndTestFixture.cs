// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;

namespace WebJobs.Script.Tests
{
    public abstract class EndToEndTestFixture : IDisposable
    {
        protected EndToEndTestFixture(string rootPath)
        {
            CreateTestStorageEntities();
            TraceWriter = new TestTraceWriter(TraceLevel.Verbose);

            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                RootPath = rootPath,
                TraceWriter = TraceWriter
            };

            Host = ScriptHost.Create(config);
            Host.Start();
        }

        private void CreateTestStorageEntities()
        {
            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);

            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
            TestQueue = queueClient.GetQueueReference("test-input");
            TestQueue.CreateIfNotExists();
            TestQueue.Clear();

            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            TestContainer = blobClient.GetContainerReference("test-output");
            TestContainer.CreateIfNotExists();
        }

        public TestTraceWriter TraceWriter { get; private set; }

        public CloudBlobContainer TestContainer { get; private set; }

        public CloudQueue TestQueue { get; private set; }

        public ScriptHost Host { get; private set; }

        public void Dispose()
        {
            Host.Stop();
            Host.Dispose();
        }
    }
}
