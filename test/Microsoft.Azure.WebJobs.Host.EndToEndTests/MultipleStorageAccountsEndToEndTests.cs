// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.EndToEndTests
{
    public class MultipleStorageAccountsEndToEndTests : IClassFixture<MultipleStorageAccountsEndToEndTests.TestFixture>
    {
        private const string TestArtifactPrefix = "e2etestmultiaccount";
        private const string Input = TestArtifactPrefix + "-input-%rnd%";
        private const string Output = TestArtifactPrefix + "-output-%rnd%";
        private const string InputTableName = TestArtifactPrefix + "tableinput%rnd%";
        private const string OutputTableName = TestArtifactPrefix + "tableinput%rnd%";
        private const string TestData = "﻿TestData";
        private const string Secondary = "SecondaryStorage";
        private static CloudStorageAccount primaryAccountResult;
        private static CloudStorageAccount secondaryAccountResult;
        private readonly TestFixture _fixture;

        public MultipleStorageAccountsEndToEndTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task BlobToBlob_DifferentAccounts_PrimaryToSecondary_Succeeds()
        {
            CloudBlockBlob resultBlob = null;

            await TestHelpers.Await(async () =>
            {
                resultBlob = (CloudBlockBlob) (await _fixture.OutputContainer2.ListBlobsSegmentedAsync("blob1", null)).Results.SingleOrDefault();
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

            await TestHelpers.Await(async () =>
            {
                resultBlob = (CloudBlockBlob) (await _fixture.OutputContainer1.ListBlobsSegmentedAsync(null)).Results.SingleOrDefault();
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

            await TestHelpers.Await(async () =>
            {
                resultMessage = await _fixture.OutputQueue2.GetMessageAsync();
                return resultMessage != null;
            });

            Assert.Equal(TestData, resultMessage.AsString);
        }

        [Theory]
        [InlineData("QueueToBlob_DifferentAccounts_PrimaryToSecondary_NameResolver")]
        [InlineData("QueueToBlob_DifferentAccounts_PrimaryToSecondary_FullSettingName")]
        public async Task QueueToBlob_DifferentAccounts_PrimaryToSecondary_NameResolver_Succeeds(string methodName)
        {
            var method = typeof(MultipleStorageAccountsEndToEndTests).GetMethod(methodName);
            string name = Guid.NewGuid().ToString();
            JObject jObject = new JObject
            {
                { "Name", name },
                { "Value", TestData }
            };
            await _fixture.Host.CallAsync(method, new { input = jObject.ToString() });

            var blobReference = await _fixture.OutputContainer2.GetBlobReferenceFromServerAsync(name);
            await TestHelpers.Await(() => blobReference.ExistsAsync());

            string data;
            using (var memoryStream = new MemoryStream())
            {
                await blobReference.DownloadToStreamAsync(memoryStream);
                memoryStream.Position = 0;
                using (var reader = new StreamReader(memoryStream, Encoding.Unicode))
                {
                    data = reader.ReadToEnd();
                }
            }
            Assert.Equal(TestData, data);
        }

        [Fact]
        public async Task QueueToQueue_DifferentAccounts_SecondaryToPrimary_Succeeds()
        {
            CloudQueueMessage resultMessage = null;

            await TestHelpers.Await(async () =>
            {
                resultMessage = await _fixture.OutputQueue1.GetMessageAsync();
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
            await TestHelpers.Await(async () =>
            {
                TableResult result = await _fixture.OutputTable1.ExecuteAsync(TableOperation.Retrieve<TestTableEntity>("test", "test"));
                if (result != null)
                {
                    entity1 = (TestTableEntity)result.Result;
                }

                result = await _fixture.OutputTable2.ExecuteAsync(TableOperation.Retrieve<TestTableEntity>("test", "test"));
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

            Assert.Equal(_fixture.Account1.Credentials.AccountName, primaryAccountResult.Credentials.AccountName);
            Assert.Equal(_fixture.Account2.Credentials.AccountName, secondaryAccountResult.Credentials.AccountName);
        }

        public static void BlobToBlob_DifferentAccounts_PrimaryToSecondary(
            [BlobTrigger(Input + "/{name}")] string input,
            [Blob(Output + "/{name}", Connection = Secondary)] out string output)
        {
            output = input;
        }

        public static void BlobToBlob_DifferentAccounts_SecondaryToPrimary(
            [BlobTrigger(Input + "/{name}", Connection = Secondary)] string input,
            [Blob(Output + "/{name}")] out string output)
        {
            output = input;
        }


        public static void QueueToQueue_DifferentAccounts_PrimaryToSecondary(
            [QueueTrigger(Input)] string input,
            [Queue(Output, Connection = Secondary)] out string output)
        {
            output = input;
        }

        [NoAutomaticTrigger]
        public static void QueueToBlob_DifferentAccounts_PrimaryToSecondary_NameResolver(
            [QueueTrigger("test")] Message input,
            [Blob(Output + "/{Name}", Connection = "%test_account%")] out string output)
        {
            output = input.Value;
        }

        [NoAutomaticTrigger]
        public static void QueueToBlob_DifferentAccounts_PrimaryToSecondary_FullSettingName(
            [QueueTrigger("test")] Message input,
            [Blob(Output + "/{Name}", Connection = "AzureWebJobsSecondaryStorage")] out string output)
        {
            output = input.Value;
        }

        public static void QueueToQueue_DifferentAccounts_SecondaryToPrimary(
            [QueueTrigger(Input, Connection = Secondary)] string input,
            [Queue(Output)] out string output)
        {
            output = input;
        }

        [NoAutomaticTrigger]
        public async static Task Table_PrimaryAndSecondary(
            [Table(OutputTableName)] CloudTable primaryOutput,
            [Table(OutputTableName, Connection = Secondary)] CloudTable secondaryOutput)
        {
            TestTableEntity entity = new TestTableEntity
            {
                PartitionKey = "test",
                RowKey = "test",
                Text = TestData
            };
            await primaryOutput.ExecuteAsync(TableOperation.InsertOrReplace(entity));
            await secondaryOutput.ExecuteAsync(TableOperation.InsertOrReplace(entity));
        }

        [NoAutomaticTrigger]
        public static void BindToCloudStorageAccount(
            CloudStorageAccount primary,
            [StorageAccount(Secondary)] CloudStorageAccount secondary)
        {
            primaryAccountResult = primary;
            secondaryAccountResult = secondary;
        }

        private class TestNameResolver : RandomNameResolver
        {
            public override string Resolve(string name)
            {
                if (name == "test_account")
                {
                    return "SecondaryStorage";
                }
                return base.Resolve(name);
            }
        }

        public class TestFixture : IDisposable
        {
            public TestFixture()
            {
                Initialize().Wait();
            }

            private async Task Initialize()
            {
                RandomNameResolver nameResolver = new TestNameResolver();
                JobHostConfiguration hostConfiguration = new JobHostConfiguration()
                {
                    NameResolver = nameResolver,
                    TypeLocator = new FakeTypeLocator(typeof(MultipleStorageAccountsEndToEndTests)),
                };

                hostConfiguration.AddService<IWebJobsExceptionHandler>(new TestExceptionHandler());

                Config = hostConfiguration;

                Account1 = CloudStorageAccount.Parse(hostConfiguration.StorageConnectionString);
                string secondaryConnectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(Secondary);
                Account2 = CloudStorageAccount.Parse(secondaryConnectionString);

                await CleanContainers();

                CloudBlobClient blobClient1 = Account1.CreateCloudBlobClient();
                string inputName = nameResolver.ResolveInString(Input);
                CloudBlobContainer inputContainer1 = blobClient1.GetContainerReference(inputName);
                await inputContainer1.CreateIfNotExistsAsync();
                string outputName = nameResolver.ResolveWholeString(Output);
                OutputContainer1 = blobClient1.GetContainerReference(outputName);
                await OutputContainer1.CreateIfNotExistsAsync();

                CloudBlobClient blobClient2 = Account2.CreateCloudBlobClient();
                CloudBlobContainer inputContainer2 = blobClient2.GetContainerReference(inputName);
                await inputContainer2.CreateIfNotExistsAsync();
                OutputContainer2 = blobClient2.GetContainerReference(outputName);
                await OutputContainer2.CreateIfNotExistsAsync();

                CloudQueueClient queueClient1 = Account1.CreateCloudQueueClient();
                CloudQueue inputQueue1 = queueClient1.GetQueueReference(inputName);
                await inputQueue1.CreateIfNotExistsAsync();
                OutputQueue1 = queueClient1.GetQueueReference(outputName);
                await OutputQueue1.CreateIfNotExistsAsync();

                CloudQueueClient queueClient2 = Account2.CreateCloudQueueClient();
                CloudQueue inputQueue2 = queueClient2.GetQueueReference(inputName);
                await inputQueue2.CreateIfNotExistsAsync();
                OutputQueue2 = queueClient2.GetQueueReference(outputName);
                await OutputQueue2.CreateIfNotExistsAsync();

                CloudTableClient tableClient1 = Account1.CreateCloudTableClient();
                string outputTableName = nameResolver.ResolveWholeString(OutputTableName);
                OutputTable1 = tableClient1.GetTableReference(outputTableName);
                OutputTable2 = Account2.CreateCloudTableClient().GetTableReference(outputTableName);

                // upload some test blobs to the input containers of both storage accounts
                CloudBlockBlob blob = inputContainer1.GetBlockBlobReference("blob1");
                await blob.UploadTextAsync(TestData);
                blob = inputContainer2.GetBlockBlobReference("blob2");
                await blob.UploadTextAsync(TestData);

                // upload some test queue messages to the input queues of both storage accounts
                await inputQueue1.AddMessageAsync(new CloudQueueMessage(TestData));
                await inputQueue2.AddMessageAsync(new CloudQueueMessage(TestData));

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

                CleanContainers().Wait();
            }

            private async Task CleanContainers()
            {
                await Clean(Account1);
                await Clean(Account2);
            }
        }

        private async static Task Clean(CloudStorageAccount account)
        {
            CloudBlobClient blobClient = account.CreateCloudBlobClient();
            foreach (var testContainer in (await blobClient.ListContainersSegmentedAsync(TestArtifactPrefix, null)).Results)
            {
                await testContainer.DeleteAsync();
            }

            CloudTableClient tableClient = account.CreateCloudTableClient();
            foreach (var table in await tableClient.ListTablesSegmentedAsync(TestArtifactPrefix, null))
            {
                await table.DeleteAsync();
            }

            CloudQueueClient queueClient = account.CreateCloudQueueClient();
            foreach (var queue in (await queueClient.ListQueuesSegmentedAsync(TestArtifactPrefix, null)).Results)
            {
                await queue.DeleteAsync();
            }
        }

        public class TestTableEntity : TableEntity
        {
            public string Text { get; set; }
        }

        public class Message
        {
            public string Name { get; set; }
            public string Value { get; set; }
        }
    }
}
