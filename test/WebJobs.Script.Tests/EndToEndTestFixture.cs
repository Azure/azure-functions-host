// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;
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
        private CloudQueueClient _queueClient;
        private CloudBlobClient _blobClient;

        protected EndToEndTestFixture(string rootPath)
        {
            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
            _queueClient = storageAccount.CreateCloudQueueClient();
            _blobClient = storageAccount.CreateCloudBlobClient();

            CreateTestStorageEntities();
            TraceWriter = new TestTraceWriter(TraceLevel.Verbose);

            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                RootScriptPath = rootPath,
                TraceWriter = TraceWriter
            };

            HostManager = new ScriptHostManager(config);

            Thread t = new Thread(_ =>
            {
                HostManager.RunAndBlock();
            });
            t.Start();

            TestHelpers.Await(() => HostManager.IsRunning).Wait();
        }

        public TestTraceWriter TraceWriter { get; private set; }

        public CloudBlobContainer TestContainer { get; private set; }

        public CloudQueue TestQueue { get; private set; }

        public ScriptHost Host
        {
            get { return HostManager.Instance; }
        }

        public ScriptHostManager HostManager { get; private set; }

        public CloudQueue GetNewQueue(string queueName)
        {
            var queue = _queueClient.GetQueueReference(queueName);
            queue.CreateIfNotExists();
            queue.Clear();
            return queue;
        }

        private void CreateTestStorageEntities()
        {
            TestQueue = _queueClient.GetQueueReference("test-input");
            TestQueue.CreateIfNotExists();
            TestQueue.Clear();

            TestContainer = _blobClient.GetContainerReference("test-output");
            TestContainer.CreateIfNotExists();
        }

        public void Dispose()
        {
            HostManager.Stop();
            HostManager.Dispose();
        }
    }
}
