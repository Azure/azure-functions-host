// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.EndToEndTests
{
    public class MultipleStorageAccountsEndToEndTests : IClassFixture<MultipleStorageAccountsEndToEndTests.TestFixture>
    {
        private const string TestArtifactPrefix = "e2etestmultiaccount";
        private const string Input = TestArtifactPrefix + "-input-%rnd%";
        private const string Output = TestArtifactPrefix + "-output-%rnd%";
        private const string InputTableName = TestArtifactPrefix + "tableinput";
        private const string OutputTableName = TestArtifactPrefix + "tableinput";
        private const string TestData = "﻿TestData";
        private const string Secondary = "SecondaryStorage";
        private static CloudStorageAccount PrimaryAccountResult;
        private static CloudStorageAccount SecondaryAccountResult;
        private readonly TestFixture _fixture;

        public MultipleStorageAccountsEndToEndTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task BlobToBlob_DifferentAccounts_PrimaryToSecondary_Succeeds()
        {
            CloudBlockBlob resultBlob = null;

            await TestHelpers.Await(() =>
            {
                resultBlob = (CloudBlockBlob)_fixture.OutputContainer2.ListBlobs().SingleOrDefault();
                return resultBlob != null;
            });

            string data = await resultBlob.DownloadTextAsync();
            Assert.Equal("blob1", resultBlob.Name);
            Assert.Equal(TestData, data);
        }

        [Fact]
        public async Task BlobToBlob_DifferentAccounts_SecondaryToPrimary_Succeeds()
        {
            CloudBlockBlob resultBlob = null;

            await TestHelpers.Await(() =>
            {
                resultBlob = (CloudBlockBlob)_fixture.OutputContainer1.ListBlobs().SingleOrDefault();
                return resultBlob != null;
            });

            string data = await resultBlob.DownloadTextAsync();
            Assert.Equal("blob2", resultBlob.Name);
            Assert.Equal(TestData, data);
        }

        [Fact]
        public async Task QueueToQueue_DifferentAccounts_PrimaryToSecondary_Succeeds()
        {
            CloudQueueMessage resultMessage = null;

            await TestHelpers.Await(() =>
            {
                resultMessage = _fixture.OutputQueue2.GetMessage();
                return resultMessage != null;
            });

            Assert.Equal(TestData, resultMessage.AsString);
        }

        [Fact]
        public async Task QueueToQueue_DifferentAccounts_SecondaryToPrimary_Succeeds()
        {
            CloudQueueMessage resultMessage = null;

            await TestHelpers.Await(() =>
            {
                resultMessage = _fixture.OutputQueue1.GetMessage();
                return resultMessage != null;
            });

            Assert.Equal(TestData, resultMessage.AsString);
        }

        [Fact]
        public async Task Table_PrimaryAndSecondary_Succeeds()
        {
            await _fixture.Host.CallAsync(typeof(MultipleStorageAccountsEndToEndTests).GetMethod("Table_PrimaryAndSecondary"));

            TestTableEntity entity1 = null;
            TestTableEntity entity2 = null;
            await TestHelpers.Await(() =>
            {
                TableResult result = _fixture.OutputTable1.Execute(TableOperation.Retrieve<TestTableEntity>("test", "test"));
                if (result != null)
                {
                    entity1 = (TestTableEntity)result.Result;
                }

                result = _fixture.OutputTable2.Execute(TableOperation.Retrieve<TestTableEntity>("test", "test"));
                if (result != null)
                {
                    entity2 = (TestTableEntity)result.Result;
                }

                return entity1 != null && entity2 != null;
            });

            Assert.Equal(TestData, entity1.Text);
            Assert.Equal(TestData, entity2.Text);
        }

        [Fact]
        public async Task CloudStorageAccount_PrimaryAndSecondary_Succeeds()
        {
            await _fixture.Host.CallAsync(typeof(MultipleStorageAccountsEndToEndTests).GetMethod("BindToCloudStorageAccount"));

            Assert.Equal(_fixture.Account1.Credentials.AccountName, PrimaryAccountResult.Credentials.AccountName);
            Assert.Equal(_fixture.Account2.Credentials.AccountName, SecondaryAccountResult.Credentials.AccountName);
        }

        public static void BlobToBlob_DifferentAccounts_PrimaryToSecondary(
            [BlobTrigger(Input + "/{name}")] string input,
            [Blob(Output + "/{name}"), StorageAccount(Secondary)] out string output)
        {
            output = input;
        }

        public static void BlobToBlob_DifferentAccounts_SecondaryToPrimary(
            [BlobTrigger(Input + "/{name}"), StorageAccount(Secondary)] string input,
            [Blob(Output + "/{name}")] out string output)
        {
            output = input;
        }


        public static void QueueToQueue_DifferentAccounts_PrimaryToSecondary(
            [QueueTrigger(Input)] string input,
            [Queue(Output), StorageAccount(Secondary)] out string output)
        {
            output = input;
        }

        public static void QueueToQueue_DifferentAccounts_SecondaryToPrimary(
            [QueueTrigger(Input), StorageAccount(Secondary)] string input,
            [Queue(Output)] out string output)
        {
            output = input;
        }

        [NoAutomaticTrigger]
        public static void Table_PrimaryAndSecondary(
            [Table(OutputTableName)] CloudTable primaryOutput,
            [Table(OutputTableName), StorageAccount(Secondary)] CloudTable secondaryOutput)
        {
            TestTableEntity entity = new TestTableEntity
            {
                PartitionKey = "test",
                RowKey = "test",
                Text = TestData
            };
            primaryOutput.Execute(TableOperation.InsertOrReplace(entity));
            secondaryOutput.Execute(TableOperation.InsertOrReplace(entity));
        }

        [NoAutomaticTrigger]
        public static void BindToCloudStorageAccount(
            CloudStorageAccount primary,
            [StorageAccount(Secondary)] CloudStorageAccount secondary)
        {
            PrimaryAccountResult = primary;
            SecondaryAccountResult = secondary;
        }

        public class TestFixture : IDisposable
        {
            public TestFixture()
            {
                RandomNameResolver nameResolver = new RandomNameResolver();
                JobHostConfiguration hostConfiguration = new JobHostConfiguration()
                {
                    NameResolver = nameResolver,
                    TypeLocator = new FakeTypeLocator(typeof(MultipleStorageAccountsEndToEndTests)),
                };
                Config = hostConfiguration;

                Account1 = CloudStorageAccount.Parse(hostConfiguration.StorageConnectionString);
                string secondaryConnectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(Secondary);
                Account2 = CloudStorageAccount.Parse(secondaryConnectionString);

                CleanContainers();

                CloudBlobClient blobClient1 = Account1.CreateCloudBlobClient();
                string inputName = nameResolver.ResolveInString(Input);
                CloudBlobContainer inputContainer1 = blobClient1.GetContainerReference(inputName);
                inputContainer1.Create();
                string outputName = nameResolver.ResolveWholeString(Output);
                OutputContainer1 = blobClient1.GetContainerReference(outputName);
                OutputContainer1.CreateIfNotExists();

                CloudBlobClient blobClient2 = Account2.CreateCloudBlobClient();
                CloudBlobContainer inputContainer2 = blobClient2.GetContainerReference(inputName);
                inputContainer2.Create();
                OutputContainer2 = blobClient2.GetContainerReference(outputName);
                OutputContainer2.CreateIfNotExists();

                CloudQueueClient queueClient1 = Account1.CreateCloudQueueClient();
                CloudQueue inputQueue1 = queueClient1.GetQueueReference(inputName);
                inputQueue1.CreateIfNotExists();
                OutputQueue1 = queueClient1.GetQueueReference(outputName);
                OutputQueue1.CreateIfNotExists();

                CloudQueueClient queueClient2 = Account2.CreateCloudQueueClient();
                CloudQueue inputQueue2 = queueClient2.GetQueueReference(inputName);
                inputQueue2.CreateIfNotExists();
                OutputQueue2 = queueClient2.GetQueueReference(outputName);
                OutputQueue2.CreateIfNotExists();

                CloudTableClient tableClient1 = Account1.CreateCloudTableClient();
                string outputTableName = nameResolver.ResolveWholeString(OutputTableName);
                OutputTable1 = tableClient1.GetTableReference(outputTableName);
                OutputTable2 = Account2.CreateCloudTableClient().GetTableReference(outputTableName);

                // upload some test blobs to the input containers of both storage accounts
                CloudBlockBlob blob = inputContainer1.GetBlockBlobReference("blob1");
                blob.UploadText(TestData);
                blob = inputContainer2.GetBlockBlobReference("blob2");
                blob.UploadText(TestData);

                // upload some test queue messages to the input queues of both storage accounts
                inputQueue1.AddMessage(new CloudQueueMessage(TestData));
                inputQueue2.AddMessage(new CloudQueueMessage(TestData));

                Host = new JobHost(hostConfiguration);
                Host.Start();
            }

            public JobHost Host
            {
                get;
                private set;
            }

            public JobHostConfiguration Config
            {
                get;
                private set;
            }

            public CloudStorageAccount Account1 { get; private set; }
            public CloudStorageAccount Account2 { get; private set; }

            public CloudBlobContainer OutputContainer1 { get; private set; }

            public CloudBlobContainer OutputContainer2 { get; private set; }

            public CloudQueue OutputQueue1 { get; private set; }

            public CloudQueue OutputQueue2 { get; private set; }

            public CloudTable OutputTable1 { get; private set; }

            public CloudTable OutputTable2 { get; private set; }

            public void Dispose()
            {
                Host.Stop();

                CleanContainers();
            }

            private void CleanContainers()
            {
                Clean(Account1);
                Clean(Account2);
            }
        }

        private static void Clean(CloudStorageAccount account)
        {
            CloudBlobClient blobClient = account.CreateCloudBlobClient();
            foreach (var testContainer in blobClient.ListContainers(TestArtifactPrefix))
            {
                testContainer.Delete();
            }

            CloudTableClient tableClient = account.CreateCloudTableClient();
            foreach (var table in tableClient.ListTables(TestArtifactPrefix))
            {
                table.Delete();
            }

            CloudQueueClient queueClient = account.CreateCloudQueueClient();
            foreach (var queue in queueClient.ListQueues(TestArtifactPrefix))
            {
                queue.Delete();
            }
        }

        public class TestTableEntity : TableEntity
        {
            public string Text { get; set; }
        }
    }
}
