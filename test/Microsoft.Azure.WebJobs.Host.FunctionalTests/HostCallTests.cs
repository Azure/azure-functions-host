// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Blobs;
using Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using Xunit;
using Xunit.Extensions;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    // Some tests in this class aren't as targeted as most other tests in this project.
    // (Look elsewhere for better examples to use as templates for new tests.)
    public class HostCallTests
    {
        private const string ContainerName = "container";
        private const string BlobName = "blob";
        private const string BlobPath = ContainerName + "/" + BlobName;
        private const string OutputBlobName = "blob.out";
        private const string OutputBlobPath = ContainerName + "/" + OutputBlobName;
        private const string OutputQueueName = "output";
        private const int TestValue = Int32.MinValue;

        [Theory]
        [InlineData("FuncWithString")]
        [InlineData("FuncWithTextReader")]
        [InlineData("FuncWithStreamRead")]
        [InlineData("FuncWithBlockBlob")]
        [InlineData("FuncWithOutStringNull")]
        [InlineData("FuncWithStreamWriteNoop")]
        [InlineData("FuncWithT")]
        [InlineData("FuncWithOutTNull")]
        [InlineData("FuncWithValueT")]
        public void Blob_IfBoundToMissingBlob_DoesNotCreate(string methodName)
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageBlobClient client = account.CreateBlobClient();
            IStorageBlobContainer container = client.GetContainerReference(ContainerName);
            IStorageBlockBlob blob = container.GetBlockBlobReference(BlobName);

            // Act
            Call(account, typeof(MissingBlobProgram), methodName, typeof(MissingBlobToCustomObjectBinder),
                typeof(MissingBlobToCustomValueBinder));

            // Assert
            Assert.False(blob.Exists());
        }

        [Theory]
        [InlineData("FuncWithOutString")]
        [InlineData("FuncWithTextWriter")]
        [InlineData("FuncWithStreamWrite")]
        [InlineData("FuncWithOutT")]
        [InlineData("FuncWithOutValueT")]
        public void Blob_IfBoundToMissingBlob_Creates(string methodName)
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageBlobClient client = account.CreateBlobClient();
            IStorageBlobContainer container = client.GetContainerReference(ContainerName);
            IStorageBlockBlob blob = container.GetBlockBlobReference(BlobName);

            // Act
            Call(account, typeof(MissingBlobProgram), methodName, typeof(MissingBlobToCustomObjectBinder),
                typeof(MissingBlobToCustomValueBinder));

            // Assert
            Assert.True(blob.Exists());
        }

        [Fact]
        public void BlobTrigger_IfHasUnboundParameter_CanCall()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageBlobClient client = account.CreateBlobClient();
            IStorageBlobContainer container = client.GetContainerReference(ContainerName);
            const string inputBlobName = "note-monday.csv";
            IStorageBlockBlob inputBlob = container.GetBlockBlobReference(inputBlobName);
            container.CreateIfNotExists();
            inputBlob.UploadText("abc");

            IDictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "values", ContainerName + "/" + inputBlobName },
                { "unbound", "test" }
            };

            // Act
            Call(account, typeof(BlobProgram), "UnboundParameter", arguments);

            IStorageBlockBlob outputBlob = container.GetBlockBlobReference("note.csv");
            string content = outputBlob.DownloadText();
            Assert.Equal("done", content);

            // $$$ Put this in its own unit test?
            Guid? guid = BlobCausalityManager.GetWriterAsync(outputBlob,
                CancellationToken.None).GetAwaiter().GetResult();

            Assert.True(guid != Guid.Empty, "Blob is missing causality information");
        }

        [Fact]
        public void Blob_IfBoundToCloudBlockBlob_CanCall()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageBlobClient client = account.CreateBlobClient();
            IStorageBlobContainer container = client.GetContainerReference(ContainerName);
            IStorageBlockBlob inputBlob = container.GetBlockBlobReference(BlobName);
            container.CreateIfNotExists();
            inputBlob.UploadText("ignore");

            // Act
            Call(account, typeof(BlobProgram), "BindToCloudBlockBlob");
        }

        [Fact]
        public void Blob_IfBoundToString_CanCall()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageBlobClient client = account.CreateBlobClient();
            IStorageBlobContainer container = client.GetContainerReference(ContainerName);
            IStorageBlockBlob inputBlob = container.GetBlockBlobReference(BlobName);
            container.CreateIfNotExists();
            inputBlob.UploadText("0,1,2");

            // Act
            Call(account, typeof(BlobProgram), "BindToString");
        }

        [Fact]
        public void Blob_IfCopiedViaString_CanCall()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageBlobClient client = account.CreateBlobClient();
            IStorageBlobContainer container = client.GetContainerReference(ContainerName);
            IStorageBlockBlob inputBlob = container.GetBlockBlobReference(BlobName);
            container.CreateIfNotExists();
            string expectedContent = "abc";
            inputBlob.UploadText(expectedContent);

            // Act
            Call(account, typeof(BlobProgram), "CopyViaString");

            // Assert
            IStorageBlockBlob outputBlob = container.GetBlockBlobReference(OutputBlobName);
            string outputContent = outputBlob.DownloadText();
            Assert.Equal(expectedContent, outputContent);
        }

        [Fact]
        public void BlobTrigger_IfCopiedViaTextReaderTextWriter_CanCall()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageBlobClient client = account.CreateBlobClient();
            IStorageBlobContainer container = client.GetContainerReference(ContainerName);
            IStorageBlockBlob inputBlob = container.GetBlockBlobReference(BlobName);
            container.CreateIfNotExists();
            string expectedContent = "abc";
            inputBlob.UploadText(expectedContent);

            // TODO: Remove argument once host.Call supports more flexibility.
            IDictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "values", BlobPath }
            };

            // Act
            Call(account, typeof(BlobProgram), "CopyViaTextReaderTextWriter", arguments);

            // Assert
            IStorageBlockBlob outputBlob = container.GetBlockBlobReference(OutputBlobName);
            string outputContent = outputBlob.DownloadText();
            Assert.Equal(expectedContent, outputContent);
        }

        [Fact]
        public void Int32Argument_CanCallViaStringParse()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IDictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "value", "15" }
            };

            // Act
            int result = Call<int>(account, typeof(UnboundInt32Program), "Call", arguments,
                (s) => UnboundInt32Program.TaskSource = s);

            Assert.Equal(15, result);
        }

        private class UnboundInt32Program
        {
            public static TaskCompletionSource<int> TaskSource { get; set; }

            [NoAutomaticTrigger]
            public static void Call(int value)
            {
                TaskSource.TrySetResult(value);
            }
        }

        [Fact]
        public void CloudStorageAccount_CanCall()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();

            // Act
            CloudStorageAccount result = Call<CloudStorageAccount>(account, typeof(CloudStorageAccountProgram),
                "BindToCloudStorageAccount", (s) => CloudStorageAccountProgram.TaskSource = s);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(account.BlobEndpoint, result.BlobEndpoint);
        }

        private class CloudStorageAccountProgram
        {
            public static TaskCompletionSource<CloudStorageAccount> TaskSource { get; set; }

            [NoAutomaticTrigger]
            public static void BindToCloudStorageAccount(CloudStorageAccount account)
            {
                TaskSource.TrySetResult(account);
            }
        }

        [Fact]
        public void Queue_IfBoundToOutPoco_CanCall()
        {
            IStorageAccount account = CreateFakeStorageAccount();

            // Act
            Call(account, typeof(QueueProgram), "BindToOutPoco");

            // Assert
            IStorageQueue queue = account.CreateQueueClient().GetQueueReference(OutputQueueName);
            IStorageQueueMessage message = queue.GetMessage();
            AssertPocoValueEqual(15, message);
        }

        [Fact]
        public void Queue_IfBoundToICollectorPoco_CanCall()
        {
            TestEnqueueMultipleMessages("BindToICollectorPoco");
        }

        [Fact]
        public void Queue_IfBoundToIAsyncCollectorPoco_CanCall()
        {
            TestEnqueueMultipleMessages("BindToIAsyncCollectorPoco");
        }

        private static void TestEnqueueMultipleMessages(string methodName)
        {
            IStorageAccount account = CreateFakeStorageAccount();

            // Act
            Call(account, typeof(QueueProgram), methodName);

            // Assert
            IStorageQueue queue = account.CreateQueueClient().GetQueueReference(OutputQueueName);
            IEnumerable<IStorageQueueMessage> messages = queue.GetMessages(messageCount: int.MaxValue);
            Assert.NotNull(messages);
            Assert.Equal(3, messages.Count());
            IEnumerable<IStorageQueueMessage> sortedMessages = messages.OrderBy((m) => m.AsString);
            IStorageQueueMessage firstMessage = sortedMessages.ElementAt(0);
            IStorageQueueMessage secondMessage = sortedMessages.ElementAt(1);
            IStorageQueueMessage thirdMessage = sortedMessages.ElementAt(2);
            AssertPocoValueEqual(10, firstMessage);
            AssertPocoValueEqual(20, secondMessage);
            AssertPocoValueEqual(30, thirdMessage);
        }

        [Fact]
        public void Queue_IfBoundToIAsyncCollector_AddEnqueuesImmediately()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();

            // Act
            Call(account, typeof(QueueProgram), "BindToIAsyncCollectorEnqueuesImmediately");
        }

        [Fact]
        public void Queue_IfBoundToCloudQueue_CanCall()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();

            // Act
            CloudQueue result = Call<CloudQueue>(account, typeof(BindToCloudQueueProgram), "BindToCloudQueue",
                (s) => BindToCloudQueueProgram.TaskSource = s);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(OutputQueueName, result.Name);
        }

        private class BindToCloudQueueProgram
        {
            public static TaskCompletionSource<CloudQueue> TaskSource { get; set; }

            public static void BindToCloudQueue([Queue(OutputQueueName)] CloudQueue queue)
            {
                TaskSource.TrySetResult(queue);
            }
        }

        [Fact]
        public void Binder_IfBindingBlobToTextWriter_CanCall()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();

            // Act
            Call(account, typeof(BindToBinderBlobTextWriterProgram), "Call");

            // Assert
            IStorageBlobContainer container = account.CreateBlobClient().GetContainerReference(ContainerName);
            IStorageBlockBlob blob = container.GetBlockBlobReference(OutputBlobName);
            string content = blob.DownloadText();
            Assert.Equal("output", content);
        }

        private class BindToBinderBlobTextWriterProgram
        {
            [NoAutomaticTrigger]
            public static void Call(IBinder binder)
            {
                TextWriter tw = binder.Bind<TextWriter>(new BlobAttribute(OutputBlobPath));
                tw.Write("output");

                // closed automatically 
            }
        }

        private static void AssertPocoValueEqual(int expectedValue, IStorageQueueMessage actualMessage)
        {
            Assert.NotNull(actualMessage);
            string content = actualMessage.AsString;
            Poco poco = JsonConvert.DeserializeObject<Poco>(content);
            Assert.NotNull(poco);
            Assert.Equal(expectedValue, poco.Value);
        }

        [Fact]
        public void BlobTrigger_IfCopiedViaPoco_CanCall()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageBlobClient client = account.CreateBlobClient();
            IStorageBlobContainer container = client.GetContainerReference(ContainerName);
            IStorageBlockBlob inputBlob = container.GetBlockBlobReference(BlobName);
            container.CreateIfNotExists();
            inputBlob.UploadText("abc");

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "input", BlobPath }
            };

            // Act
            Call(account, typeof(CopyBlobViaPocoProgram), "CopyViaPoco", arguments, typeof(PocoBlobBinder));

            // Assert
            IStorageBlockBlob outputBlob = container.GetBlockBlobReference(OutputBlobName);
            string content = outputBlob.DownloadText();
            Assert.Equal("*abc*", content);
        }

        private class CopyBlobViaPocoProgram
        {
            public static void CopyViaPoco(
                [BlobTrigger(BlobPath)] PocoBlob input,
                [Blob(OutputBlobPath)] out PocoBlob output)
            {
                output = new PocoBlob { Value = "*" + input.Value + "*" };
            }
        }

        private class PocoBlob
        {
            public string Value;
        }

        private class PocoBlobBinder : ICloudBlobStreamBinder<PocoBlob>
        {
            public async Task<PocoBlob> ReadFromStreamAsync(Stream input, CancellationToken cancellationToken)
            {
                TextReader reader = new StreamReader(input);
                string text = await reader.ReadToEndAsync();
                return new PocoBlob { Value = text };
            }

            public async Task WriteToStreamAsync(PocoBlob value, Stream output, CancellationToken cancellationToken)
            {
                TextWriter writer = new StreamWriter(output);
                await writer.WriteAsync(value.Value);
                await writer.FlushAsync();
            }
        }

        private static void Call(IStorageAccount account, Type programType, string methodName,
            params Type[] cloudBlobStreamBinderTypes)
        {
            FunctionalTest.Call(account, programType, programType.GetMethod(methodName), arguments: null,
                cloudBlobStreamBinderTypes: cloudBlobStreamBinderTypes);
        }

        private static void Call(IStorageAccount account, Type programType, string methodName,
            IDictionary<string, object> arguments, params Type[] cloudBlobStreamBinderTypes)
        {
            FunctionalTest.Call(account, programType, programType.GetMethod(methodName), arguments,
                cloudBlobStreamBinderTypes);
        }

        private static TResult Call<TResult>(IStorageAccount account, Type programType, string methodName,
            Action<TaskCompletionSource<TResult>> setTaskSource)
        {
            IDictionary<string, object> arguments = null;
            return FunctionalTest.Call<TResult>(account, programType, programType.GetMethod(methodName), arguments,
                setTaskSource);
        }

        private static TResult Call<TResult>(IStorageAccount account, Type programType, string methodName,
            IDictionary<string, object> arguments, Action<TaskCompletionSource<TResult>> setTaskSource)
        {
            return FunctionalTest.Call<TResult>(account, programType, programType.GetMethod(methodName), arguments,
                setTaskSource);
        }

        private static IStorageAccount CreateFakeStorageAccount()
        {
            return new FakeStorageAccount();
        }

        private struct CustomDataValue
        {
            public int ValueId { get; set; }
            public string Content { get; set; }
        }

        private class CustomDataObject
        {
            public int ValueId { get; set; }
            public string Content { get; set; }
        }

        private class MissingBlobToCustomObjectBinder : ICloudBlobStreamBinder<CustomDataObject>
        {
            public Task<CustomDataObject> ReadFromStreamAsync(Stream input, CancellationToken cancellationToken)
            {
                Assert.Null(input);

                CustomDataObject value = new CustomDataObject { ValueId = TestValue };
                return Task.FromResult(value);
            }

            public Task WriteToStreamAsync(CustomDataObject value, Stream output, CancellationToken cancellationToken)
            {
                Assert.NotNull(output);

                if (value != null)
                {
                    Assert.Equal(TestValue, value.ValueId);

                    const byte ignore = 0xFF;
                    output.WriteByte(ignore);
                }

                return Task.FromResult(0);
            }
        }

        private class MissingBlobToCustomValueBinder : ICloudBlobStreamBinder<CustomDataValue>
        {
            public Task<CustomDataValue> ReadFromStreamAsync(Stream input, CancellationToken cancellationToken)
            {
                Assert.Null(input);

                CustomDataValue value = new CustomDataValue { ValueId = TestValue };
                return Task.FromResult(value);
            }

            public Task WriteToStreamAsync(CustomDataValue value, Stream output, CancellationToken cancellationToken)
            {
                Assert.NotNull(output);

                Assert.Equal(TestValue, value.ValueId);

                const byte ignore = 0xFF;
                output.WriteByte(ignore);

                return Task.FromResult(0);
            }
        }

        private class MissingBlobProgram
        {
            public static void FuncWithBlockBlob([Blob(BlobPath)] CloudBlockBlob blob)
            {
                Assert.NotNull(blob);
                Assert.Equal(BlobName, blob.Name);
                Assert.Equal(ContainerName, blob.Container.Name);
            }

            public static void FuncWithStreamRead([Blob(BlobPath, FileAccess.Read)] Stream stream)
            {
                Assert.Null(stream);
            }

            public static void FuncWithStreamWrite([Blob(BlobPath, FileAccess.Write)] Stream stream)
            {
                Assert.NotNull(stream);

                const byte ignore = 0xFF;
                stream.WriteByte(ignore);
            }

            public static void FuncWithStreamWriteNoop([Blob(BlobPath, FileAccess.Write)] Stream stream)
            {
                Assert.NotNull(stream);
            }

            public static void FuncWithTextReader([Blob(BlobPath)] TextReader reader)
            {
                Assert.Null(reader);
            }

            public static void FuncWithTextWriter([Blob(BlobPath)] TextWriter writer)
            {
                Assert.NotNull(writer);
            }

            public static void FuncWithString([Blob(BlobPath)] string content)
            {
                Assert.Null(content);
            }

            public static void FuncWithOutString([Blob(BlobPath)] out string content)
            {
                content = "ignore";
            }

            public static void FuncWithOutStringNull([Blob(BlobPath)] out string content)
            {
                content = null;
            }

            public static void FuncWithT([Blob(BlobPath)] CustomDataObject value)
            {
                Assert.NotNull(value);
                Assert.Equal(TestValue, value.ValueId);
            }

            public static void FuncWithOutT([Blob(BlobPath)] out CustomDataObject value)
            {
                value = new CustomDataObject { ValueId = TestValue, Content = "ignore" };
            }

            public static void FuncWithOutTNull([Blob(BlobPath)] out CustomDataObject value)
            {
                value = null;
            }

            public static void FuncWithValueT([Blob(BlobPath)] CustomDataValue value)
            {
                Assert.NotNull(value);
                Assert.Equal(TestValue, value.ValueId);
            }

            public static void FuncWithOutValueT([Blob(BlobPath)] out CustomDataValue value)
            {
                value = new CustomDataValue { ValueId = TestValue, Content = "ignore" };
            }
        }

        private class BlobProgram
        {
            // This can be invoked explicitly (and providing parameters)
            // or it can be invoked implicitly by triggering on input. // (assuming no unbound parameters)
            [NoAutomaticTrigger]
            public static void UnboundParameter(
                string name, string date,  // used by input
                string unbound, // not used by in/out
                [BlobTrigger(ContainerName + "/{name}-{date}.csv")] TextReader values,
                [Blob(ContainerName + "/{name}.csv")] TextWriter output
                )
            {
                Assert.Equal("test", unbound);
                Assert.Equal("note", name);
                Assert.Equal("monday", date);

                string content = values.ReadToEnd();
                Assert.Equal("abc", content);

                output.Write("done");
            }

            public static void BindToCloudBlockBlob([Blob(BlobPath)] CloudBlockBlob blob)
            {
                Assert.NotNull(blob);
                Assert.Equal(BlobName, blob.Name);
            }

            public static void BindToString([Blob(BlobPath)] string content)
            {
                Assert.NotNull(content);
                string[] strings = content.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                // Verify expected number of entries in CloudBlob
                Assert.Equal(3, strings.Length);
                for (int i = 0; i < 3; ++i)
                {
                    int value;
                    bool parsed = int.TryParse(strings[i], out value);
                    string message = String.Format("Unable to parse CloudBlob strings[{0}]: '{1}'", i, strings[i]);
                    Assert.True(parsed, message);
                    // Ensure expected value in CloudBlob
                    Assert.Equal(i, value);
                }
            }

            public static void CopyViaString(
                [Blob(BlobPath)] string blobIn,
                [Blob(OutputBlobPath)] out string blobOut
                )
            {
                blobOut = blobIn;
            }

            public static void CopyViaTextReaderTextWriter(
                [BlobTrigger(BlobPath)] TextReader values,
                [Blob(OutputBlobPath)] TextWriter output)
            {
                string content = values.ReadToEnd();
                output.Write(content);
            }
        }

        private class QueueProgram
        {
            public static void BindToOutPoco([Queue(OutputQueueName)] out Poco output)
            {
                output = new Poco { Value = 15 };
            }

            public static void BindToICollectorPoco([Queue(OutputQueueName)] ICollector<Poco> output)
            {
                output.Add(new Poco { Value = 10 });
                output.Add(new Poco { Value = 20 });
                output.Add(new Poco { Value = 30 });
            }

            public static async Task BindToIAsyncCollectorPoco(
                [Queue(OutputQueueName)] IAsyncCollector<Poco> output)
            {
                await output.AddAsync(new Poco { Value = 10 });
                await output.AddAsync(new Poco { Value = 20 });
                await output.AddAsync(new Poco { Value = 30 });
            }

            public static async Task BindToIAsyncCollectorEnqueuesImmediately(
                [Queue(OutputQueueName)] IAsyncCollector<string> collector,
                [Queue(OutputQueueName)] IStorageQueue queue)
            {
                string expectedContents = "Enqueued immediately";
                await collector.AddAsync(expectedContents);
                IStorageQueueMessage message = queue.GetMessage();
                Assert.NotNull(message);
                Assert.Equal(expectedContents, message.AsString);
            }
        }

        private class Poco
        {
            public int Value { get; set; }
        }
    }
}
