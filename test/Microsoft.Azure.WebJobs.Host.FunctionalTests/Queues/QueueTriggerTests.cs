// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    public class QueueTriggerTests
    {
        private const string QueueName = "input";

        [Fact]
        public void QueueTrigger_IfBoundToCloudQueueMessage_Binds()
        {
            // Arrange
            string expectedGuid = Guid.NewGuid().ToString();
            CloudQueueMessage expectedMessage = new CloudQueueMessage(expectedGuid);
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            queue.AddMessage(new FakeStorageQueueMessage(expectedMessage));

            // Act
            CloudQueueMessage result = RunTrigger<CloudQueueMessage>(account, typeof(BindToCloudQueueMessageProgram),
                (s) => BindToCloudQueueMessageProgram.TaskSource = s);

            // Assert
            Assert.Equal(expectedMessage.AsString, result.AsString);
        }

        [Fact]
        public void QueueTrigger_IfBoundToString_Binds()
        {
            string expectedContent = Guid.NewGuid().ToString();
            TestBindToString(expectedContent);
        }

        [Fact]
        public void QueueTrigger_IfBoundToStringAndMessageIsEmpty_Binds()
        {
            TestBindToString(String.Empty);
        }

        private static void TestBindToString(string expectedContent)
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            IStorageQueueMessage message = queue.CreateMessage(expectedContent);
            queue.AddMessage(message);

            // Act
            string result = RunTrigger<string>(account, typeof(BindToStringProgram),
                (s) => BindToStringProgram.TaskSource = s);

            // Assert
            Assert.Equal(expectedContent, result);
        }

        [Fact]
        public void QueueTrigger_IfBoundToStringAndMessageIsNotUtf8ByteArray_DoesNotBind()
        {
            // Arrange
            byte[] content = new byte[] { 0xFF, 0x00 }; // Not a valid UTF-8 byte sequence.
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            IStorageQueueMessage message = queue.CreateMessage(content);
            queue.AddMessage(message);

            // Act
            Exception exception = RunTriggerFailure<string>(account, typeof(BindToStringProgram),
                (s) => BindToStringProgram.TaskSource = s);

            // Assert
            Assert.IsType<InvalidOperationException>(exception);
            Assert.Equal("Exception binding parameter 'message'", exception.Message);
            Exception innerException = exception.InnerException;
            Assert.IsType<DecoderFallbackException>(innerException);
            Assert.Equal("Unable to translate bytes [FF] at index -1 from specified code page to Unicode.",
                innerException.Message);
        }

        [Fact]
        public void QueueTrigger_IfBoundToByteArray_Binds()
        {
            byte[] expectedContent = new byte[] { 0x31, 0x32, 0x33 };
            TestBindToByteArray(expectedContent);
        }

        [Fact]
        public void QueueTrigger_IfBoundToByteArrayAndMessageIsEmpty_Binds()
        {
            byte[] expectedContent = new byte[0];
            TestBindToByteArray(expectedContent);
        }

        [Fact]
        public void QueueTrigger_IfBoundToByteArrayAndMessageIsNonUtf8_Binds()
        {
            byte[] expectedContent = new byte[] { 0xFF, 0x00 }; // Not a valid UTF-8 byte sequence.
            TestBindToByteArray(expectedContent);
        }

        private static void TestBindToByteArray(byte[] expectedContent)
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            IStorageQueueMessage message = queue.CreateMessage(expectedContent);
            queue.AddMessage(message);

            // Act
            byte[] result = RunTrigger<byte[]>(account, typeof(BindToByteArrayProgram),
                (s) => BindToByteArrayProgram.TaskSource = s);

            // Assert
            Assert.Equal(expectedContent, result);
        }

        [Fact]
        public void QueueTrigger_IfBoundToPoco_Binds()
        {
            Poco expectedContent = new Poco { Value = "abc" };
            TestBindToPoco(expectedContent);
        }

        [Fact]
        public void QueueTrigger_IfBoundToPocoAndMessageIsJsonNull_Binds()
        {
            Poco expectedContent = null;
            TestBindToPoco(expectedContent);
        }

        private static void TestBindToPoco(Poco expectedContent)
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            string content = JsonConvert.SerializeObject(expectedContent, typeof(Poco), settings: null);
            IStorageQueueMessage message = queue.CreateMessage(content);
            queue.AddMessage(message);

            // Act
            Poco result = RunTrigger<Poco>(account, typeof(BindToPocoProgram), (s) => BindToPocoProgram.TaskSource = s);

            // Assert
            AssertEqual(expectedContent, result);
        }

        private static void AssertEqual(Poco expected, Poco actual)
        {
            if (expected == null)
            {
                Assert.Null(actual);
                return;
            }

            Assert.NotNull(actual);
            Assert.Equal(expected.Value, actual.Value);
            Assert.Equal(expected.Int32Value, actual.Int32Value);
            AssertEqual(expected.Child, actual.Child);
        }

        [Fact]
        public void QueueTrigger_IfBoundToPocoAndMessageIsNotJson_DoesNotBind()
        {
            // Arrange
            const string content = "not json"; // Not a valid JSON byte sequence.
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            IStorageQueueMessage message = queue.CreateMessage(content);
            queue.AddMessage(message);

            // Act
            Exception exception = RunTriggerFailure<Poco>(account, typeof(BindToPocoProgram),
                (s) => BindToPocoProgram.TaskSource = s);

            // Assert
            Assert.IsType<InvalidOperationException>(exception);
            Assert.Equal("Exception binding parameter 'message'", exception.Message);
            Exception innerException = exception.InnerException;
            Assert.IsType<InvalidOperationException>(innerException);
            const string expectedInnerMessage = "Binding parameters to complex objects (such as 'Poco') uses " +
                "Json.NET serialization. \r\n1. Bind the parameter type as 'string' instead of 'Poco' to get the raw " +
                "values and avoid JSON deserialization, or\r\n2. Change the queue payload to be valid json. The JSON " +
                "parser failed: Unexpected character encountered while parsing value: n. Path '', line 0, position " +
                "0.\r\n";
            Assert.Equal(expectedInnerMessage, innerException.Message);
        }

        [Fact]
        public void QueueTrigger_IfBoundToPocoAndMessageIsIncompatibleJson_DoesNotBind()
        {
            // Arrange
            const string content = "123"; // A JSON int rather than a JSON object.
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            IStorageQueueMessage message = queue.CreateMessage(content);
            queue.AddMessage(message);

            // Act
            Exception exception = RunTriggerFailure<Poco>(account, typeof(BindToPocoProgram),
                (s) => BindToPocoProgram.TaskSource = s);

            // Assert
            Assert.IsType<InvalidOperationException>(exception);
            Assert.Equal("Exception binding parameter 'message'", exception.Message);
            Exception innerException = exception.InnerException;
            Assert.IsType<InvalidOperationException>(innerException);
            string expectedInnerMessage = "Binding parameters to complex objects (such as 'Poco') uses Json.NET " +
                "serialization. \r\n1. Bind the parameter type as 'string' instead of 'Poco' to get the raw values " +
                "and avoid JSON deserialization, or\r\n2. Change the queue payload to be valid json. The JSON parser " +
                "failed: Error converting value 123 to type '" + typeof(Poco).FullName + "'. Path '', line 1, " +
                "position 3.\r\n";
            Assert.Equal(expectedInnerMessage, innerException.Message);
        }

        [Fact]
        public void QueueTrigger_IfBoundToPocoStruct_Binds()
        {
            // Arrange
            const int expectedContent = 123;
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            string content = JsonConvert.SerializeObject(expectedContent, typeof(int), settings: null);
            IStorageQueueMessage message = queue.CreateMessage(content);
            queue.AddMessage(message);

            // Act
            int result = RunTrigger<int>(account, typeof(BindToPocoStructProgram),
                (s) => BindToPocoStructProgram.TaskSource = s);

            // Assert
            Assert.Equal(expectedContent, result);
        }

        [Fact]
        public void QueueTrigger_IfMessageIsString_ProvidesQueueTriggerBindingData()
        {
            // Arrange
            string expectedQueueTrigger = Guid.NewGuid().ToString();
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            IStorageQueueMessage message = queue.CreateMessage(expectedQueueTrigger);
            queue.AddMessage(message);

            // Act
            string result = RunTrigger<string>(account, typeof(BindToQueueTriggerBindingDataProgram),
                (s) => BindToQueueTriggerBindingDataProgram.TaskSource = s);

            // Assert
            Assert.Equal(expectedQueueTrigger, result);
        }

        [Fact]
        public void QueueTrigger_IfMessageIsUtf8ByteArray_ProvidesQueueTriggerBindingData()
        {
            // Arrange
            const string expectedQueueTrigger = "abc";
            byte[] content = StrictEncodings.Utf8.GetBytes(expectedQueueTrigger);
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            IStorageQueueMessage message = queue.CreateMessage(content);
            queue.AddMessage(message);

            // Act
            string result = RunTrigger<string>(account, typeof(BindToQueueTriggerBindingDataProgram),
                (s) => BindToQueueTriggerBindingDataProgram.TaskSource = s);

            // Assert
            Assert.Equal(expectedQueueTrigger, result);
        }

        [Fact]
        public void QueueTrigger_IfMessageIsNonUtf8ByteArray_DoesNotProvideQueueTriggerBindingData()
        {
            // Arrange
            byte[] content = new byte[] { 0xFF, 0x00 }; // Not a valid UTF-8 byte sequence.
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            IStorageQueueMessage message = queue.CreateMessage(content);
            queue.AddMessage(message);

            // Act
            Exception exception = RunTriggerFailure<string>(account, typeof(BindToQueueTriggerBindingDataProgram),
                (s) => BindToQueueTriggerBindingDataProgram.TaskSource = s);

            // Assert
            Assert.IsType<InvalidOperationException>(exception);
            Assert.Equal("Exception binding parameter 'queueTrigger'", exception.Message);
            Exception innerException = exception.InnerException;
            Assert.IsType<InvalidOperationException>(innerException);
            Assert.Equal("Binding data does not contain expected value 'queueTrigger'.", innerException.Message);
        }

        [Fact]
        public void QueueTrigger_ProvidesDequeueCountBindingData()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            IStorageQueueMessage message = queue.CreateMessage("ignore");
            // message.DequeueCount is provided by FakeStorageAccount when the message is retrieved.
            queue.AddMessage(message);

            // Act
            int result = RunTrigger<int>(account, typeof(BindToDequeueCountBindingDataProgram),
                (s) => BindToDequeueCountBindingDataProgram.TaskSource = s);

            // Assert
            Assert.Equal(1, result);
        }

        [Fact]
        public void QueueTrigger_ProvidesExpirationTimeBindingData()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            IStorageQueueMessage message = queue.CreateMessage("ignore");
            // message.ExpirationTime is provided by FakeStorageAccount when the message is inserted.
            queue.AddMessage(message);

            // Act
            DateTimeOffset result = RunTrigger<DateTimeOffset>(account, typeof(BindToExpirationTimeBindingDataProgram),
                (s) => BindToExpirationTimeBindingDataProgram.TaskSource = s);

            // Assert
            Assert.Equal(0, (int)DateTimeOffset.Now.AddDays(7).Subtract(result).TotalDays);
        }

        [Fact]
        public void QueueTrigger_ProvidesIdBindingData()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            IStorageQueueMessage message = queue.CreateMessage("ignore");
            // message.Id is provided by FakeStorageAccount when the message is inserted.
            queue.AddMessage(message);

            // Act
            string result = RunTrigger<string>(account, typeof(BindToIdBindingDataProgram),
                (s) => BindToIdBindingDataProgram.TaskSource = s);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
        }

        [Fact]
        public void QueueTrigger_ProvidesInsertionTimeBindingData()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            IStorageQueueMessage message = queue.CreateMessage("ignore");
            // message.InsertionTime is provided by FakeStorageAccount when the message is inserted.
            queue.AddMessage(message);

            // Act
            DateTimeOffset result = RunTrigger<DateTimeOffset>(account, typeof(BindToInsertionTimeBindingDataProgram),
                (s) => BindToInsertionTimeBindingDataProgram.TaskSource = s);

            // Assert
            Assert.Equal(0, (int)DateTimeOffset.Now.Subtract(result).TotalHours);
        }

        [Fact]
        public void QueueTrigger_ProvidesNextVisibleTimeBindingData()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            IStorageQueueMessage message = queue.CreateMessage("ignore");
            // message.NextVisibleTime is provided by FakeStorageAccount when the message is retrieved.
            queue.AddMessage(message);

            // Act
            DateTimeOffset result = RunTrigger<DateTimeOffset>(account, typeof(BindToNextVisibleTimeBindingDataProgram),
                (s) => BindToNextVisibleTimeBindingDataProgram.TaskSource = s);

            // Assert
            Assert.Equal(0, (int)DateTimeOffset.Now.Subtract(result).TotalHours);
        }

        [Fact]
        public void QueueTrigger_ProvidesPopReceiptBindingData()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            IStorageQueueMessage message = queue.CreateMessage("ignore");
            // message.PopReceipt is provided by FakeStorageAccount when the message is retrieved.
            queue.AddMessage(message);

            // Act
            string result = RunTrigger<string>(account, typeof(BindToPopReceiptBindingDataProgram),
                (s) => BindToPopReceiptBindingDataProgram.TaskSource = s);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
        }

        [Fact]
        public void QueueTrigger_ProvidesPocoStructPropertyBindingData()
        {
            // Arrange
            const int expectedInt32Value = 123;
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            Poco value = new Poco { Int32Value = expectedInt32Value };
            string content = JsonConvert.SerializeObject(value, typeof(Poco), settings: null);
            IStorageQueueMessage message = queue.CreateMessage(content);
            queue.AddMessage(message);

            // Act
            int result = RunTrigger<int>(account, typeof(BindToPocoStructPropertyBindingDataProgram),
                (s) => BindToPocoStructPropertyBindingDataProgram.TaskSource = s);

            // Assert
            Assert.Equal(expectedInt32Value, result);
        }

        [Fact]
        public void QueueTrigger_ProvidesPocoComplexPropertyBindingData()
        {
            // Arrange
            Poco expectedChild = new Poco
            {
                Value = "abc",
                Int32Value = 123
            };
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            Poco value = new Poco { Child = expectedChild };
            string content = JsonConvert.SerializeObject(value, typeof(Poco), settings: null);
            IStorageQueueMessage message = queue.CreateMessage(content);
            queue.AddMessage(message);

            // Act
            Poco result = RunTrigger<Poco>(account, typeof(BindToPocoComplexPropertyBindingDataProgram),
                (s) => BindToPocoComplexPropertyBindingDataProgram.TaskSource = s);

            // Assert
            AssertEqual(expectedChild, result);
        }

        [Fact]
        public void QueueTrigger_IfBindingAlwaysFails_MovesToPoisonQueue()
        {
            // Arrange
            const string expectedContents = "abc";
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            queue.AddMessage(queue.CreateMessage(expectedContents));

            // Act
            string result = RunTrigger<string>(account, typeof(PoisonQueueProgram),
                (s) => PoisonQueueProgram.TaskSource = s,
                new string[] { typeof(PoisonQueueProgram).FullName + ".PutInPoisonQueue" });

            // Assert
            Assert.Equal(expectedContents, result);
        }

        [Fact]
        public void QueueTrigger_IfDequeueCountReachesMaxDequeueCount_MovesToPoisonQueue()
        {
            try
            {
                // Arrange
                const string expectedContents = "abc";
                IStorageAccount account = CreateFakeStorageAccount();
                IStorageQueue queue = CreateQueue(account, QueueName);
                queue.AddMessage(queue.CreateMessage(expectedContents));

                // Act
                RunTrigger<object>(account, typeof(MaxDequeueCountProgram),
                    (s) => MaxDequeueCountProgram.TaskSource = s,
                    new string[] { typeof(MaxDequeueCountProgram).FullName + ".PutInPoisonQueue" });

                // Assert
                IStorageAccountProvider storageAccountProvider = new FakeStorageAccountProvider();
                Assert.Equal(new FakeQueueConfiguration(storageAccountProvider).MaxDequeueCount, MaxDequeueCountProgram.DequeueCount);

            }
            finally
            {
                MaxDequeueCountProgram.DequeueCount = 0;
            }
        }

        [Fact]
        public void CallQueueTrigger_IfArgumentIsCloudQueueMessage_Binds()
        {
            // Arrange
            CloudQueueMessage expectedMessage = new CloudQueueMessage("ignore");

            // Act
            CloudQueueMessage result = CallQueueTrigger<CloudQueueMessage>(expectedMessage,
                typeof(BindToCloudQueueMessageProgram), (s) => BindToCloudQueueMessageProgram.TaskSource = s);

            // Assert
            Assert.Same(expectedMessage, result);
        }

        [Fact]
        public void CallQueueTrigger_IfArgumentIsString_Binds()
        {
            // Arrange
            const string expectedContents = "abc";

            // Act
            CloudQueueMessage result = CallQueueTrigger<CloudQueueMessage>(expectedContents,
                typeof(BindToCloudQueueMessageProgram), (s) => BindToCloudQueueMessageProgram.TaskSource = s);

            // Assert
            Assert.NotNull(result);
            Assert.Same(expectedContents, result.AsString);
        }

        [Fact]
        public void CallQueueTrigger_IfArgumentIsIStorageQueueMessage_Binds()
        {
            // Arrange
            CloudQueueMessage expectedMessage = new CloudQueueMessage("ignore");
            IStorageQueueMessage message = new FakeStorageQueueMessage(expectedMessage);

            // Act
            CloudQueueMessage result = CallQueueTrigger<CloudQueueMessage>(message,
                typeof(BindToCloudQueueMessageProgram), (s) => BindToCloudQueueMessageProgram.TaskSource = s);

            // Assert
            Assert.Same(expectedMessage, result);
        }

        [Fact]
        public void CallQueueTrigger_ProvidesDequeueCountBindingData()
        {
            // Arrange
            const int expectedDequeueCount = 123;
            FakeStorageQueueMessage message = new FakeStorageQueueMessage(new CloudQueueMessage("ignore"))
            {
                DequeueCount = expectedDequeueCount
            };

            // Act
            int result = CallQueueTrigger<int>(message, typeof(BindToDequeueCountBindingDataProgram),
                (s) => BindToDequeueCountBindingDataProgram.TaskSource = s);

            // Assert
            Assert.Equal(expectedDequeueCount, result);
        }

        [Fact]
        public void CallQueueTrigger_ProvidesExpirationTimeBindingData()
        {
            // Arrange
            DateTimeOffset expectedExpirationTime = DateTimeOffset.Now;
            FakeStorageQueueMessage message = new FakeStorageQueueMessage(new CloudQueueMessage("ignore"))
            {
                ExpirationTime = expectedExpirationTime
            };

            // Act
            DateTimeOffset result = CallQueueTrigger<DateTimeOffset>(message,
                typeof(BindToExpirationTimeBindingDataProgram),
                (s) => BindToExpirationTimeBindingDataProgram.TaskSource = s);

            // Assert
            Assert.Equal(expectedExpirationTime, result);
        }

        [Fact]
        public void CallQueueTrigger_IfExpirationTimeIsNull_ProvidesMaxValueExpirationTimeBindingData()
        {
            // Arrange
            DateTimeOffset expectedExpirationTime = DateTimeOffset.Now;
            FakeStorageQueueMessage message = new FakeStorageQueueMessage(new CloudQueueMessage("ignore"))
            {
                ExpirationTime = null
            };

            // Act
            DateTimeOffset result = CallQueueTrigger<DateTimeOffset>(message,
                typeof(BindToExpirationTimeBindingDataProgram),
                (s) => BindToExpirationTimeBindingDataProgram.TaskSource = s);

            // Assert
            Assert.Equal(DateTimeOffset.MaxValue, result);
        }

        [Fact]
        public void CallQueueTrigger_ProvidesIdBindingData()
        {
            // Arrange
            const string expectedId = "abc";
            FakeStorageQueueMessage message = new FakeStorageQueueMessage(new CloudQueueMessage("ignore"))
            {
                Id = expectedId
            };

            // Act
            string result = CallQueueTrigger<string>(message, typeof(BindToIdBindingDataProgram),
                (s) => BindToIdBindingDataProgram.TaskSource = s);

            // Assert
            Assert.Same(expectedId, result);
        }

        [Fact]
        public void CallQueueTrigger_ProvidesInsertionTimeBindingData()
        {
            // Arrange
            DateTimeOffset expectedInsertionTime = DateTimeOffset.Now;
            FakeStorageQueueMessage message = new FakeStorageQueueMessage(new CloudQueueMessage("ignore"))
            {
                InsertionTime = expectedInsertionTime
            };

            // Act
            DateTimeOffset result = CallQueueTrigger<DateTimeOffset>(message,
                typeof(BindToInsertionTimeBindingDataProgram),
                (s) => BindToInsertionTimeBindingDataProgram.TaskSource = s);

            // Assert
            Assert.Equal(expectedInsertionTime, result);
        }

        [Fact]
        public void CallQueueTrigger_IfInsertionTimeIsNull_ProvidesUtcNowInsertionTimeBindingData()
        {
            // Arrange
            DateTimeOffset expectedInsertionTime = DateTimeOffset.Now;
            FakeStorageQueueMessage message = new FakeStorageQueueMessage(new CloudQueueMessage("ignore"))
            {
                InsertionTime = null
            };

            // Act
            DateTimeOffset result = CallQueueTrigger<DateTimeOffset>(message,
                typeof(BindToInsertionTimeBindingDataProgram),
                (s) => BindToInsertionTimeBindingDataProgram.TaskSource = s);

            // Assert
            Assert.Equal(0, (int)DateTimeOffset.Now.Subtract(result).TotalMinutes);
            Assert.Equal(TimeSpan.Zero, result.Offset);
        }

        [Fact]
        public void CallQueueTrigger_ProvidesNextVisibleTimeBindingData()
        {
            // Arrange
            DateTimeOffset expectedNextVisibleTime = DateTimeOffset.Now;
            FakeStorageQueueMessage message = new FakeStorageQueueMessage(new CloudQueueMessage("ignore"))
            {
                NextVisibleTime = expectedNextVisibleTime
            };

            // Act
            DateTimeOffset result = CallQueueTrigger<DateTimeOffset>(message,
                typeof(BindToNextVisibleTimeBindingDataProgram),
                (s) => BindToNextVisibleTimeBindingDataProgram.TaskSource = s);

            // Assert
            Assert.Equal(expectedNextVisibleTime, result);
        }

        [Fact]
        public void CallQueueTrigger_IfNextVisibleTimeIsNull_ProvidesMaxValueNextVisibleTimeBindingData()
        {
            // Arrange
            DateTimeOffset expectedNextVisibleTime = DateTimeOffset.Now;
            FakeStorageQueueMessage message = new FakeStorageQueueMessage(new CloudQueueMessage("ignore"))
            {
                NextVisibleTime = null
            };

            // Act
            DateTimeOffset result = CallQueueTrigger<DateTimeOffset>(message,
                typeof(BindToNextVisibleTimeBindingDataProgram),
                (s) => BindToNextVisibleTimeBindingDataProgram.TaskSource = s);

            // Assert
            Assert.Equal(DateTimeOffset.MaxValue, result);
        }

        [Fact]
        public void CallQueueTrigger_ProvidesPopReceiptBindingData()
        {
            // Arrange
            const string expectedPopReceipt = "abc";
            FakeStorageQueueMessage message = new FakeStorageQueueMessage(new CloudQueueMessage("ignore"))
            {
                PopReceipt = expectedPopReceipt
            };

            // Act
            string result = CallQueueTrigger<string>(message, typeof(BindToPopReceiptBindingDataProgram),
                (s) => BindToPopReceiptBindingDataProgram.TaskSource = s);

            // Assert
            Assert.Same(expectedPopReceipt, result);
        }

        private static TResult RunTrigger<TResult>(IStorageAccount account, Type programType,
            Action<TaskCompletionSource<TResult>> setTaskSource)
        {
            return FunctionalTest.RunTrigger<TResult>(account, programType, setTaskSource);
        }

        private static TResult RunTrigger<TResult>(IStorageAccount account, Type programType,
            Action<TaskCompletionSource<TResult>> setTaskSource, IEnumerable<string> ignoreFailureFunctions)
        {
            return FunctionalTest.RunTrigger<TResult>(account, programType, setTaskSource, ignoreFailureFunctions);
        }

        private static Exception RunTriggerFailure<TResult>(IStorageAccount account, Type programType,
            Action<TaskCompletionSource<TResult>> setTaskSource)
        {
            return FunctionalTest.RunTriggerFailure<TResult>(account, programType, setTaskSource);
        }

        private static TResult CallQueueTrigger<TResult>(object message, Type programType,
            Action<TaskCompletionSource<TResult>> setTaskSource)
        {
            // Arrange
            TaskCompletionSource<TResult> functionTaskSource = new TaskCompletionSource<TResult>();
            IServiceProvider serviceProvider = FunctionalTest.CreateServiceProviderForManualCompletion<TResult>(
                CreateFakeStorageAccount(), programType, functionTaskSource);
            Task<TResult> functionTask = functionTaskSource.Task;
            setTaskSource.Invoke(functionTaskSource);
            Task callTask;
            bool completed;

            using (JobHost host = new JobHost(serviceProvider))
            {
                try
                {
                    callTask = host.CallAsync(programType.GetMethod("Run"), new { message = message });

                    // Act
                    completed = Task.WhenAll(callTask, functionTask).WaitUntilCompleted(3 * 1000);
                }
                finally
                {
                    setTaskSource.Invoke(null);
                }
            }

            // Assert
            Assert.True(completed);

            // Give a nicer test failure message for faulted tasks.
            if (functionTask.Status == TaskStatus.Faulted)
            {
                functionTask.GetAwaiter().GetResult();
            }

            Assert.Equal(TaskStatus.RanToCompletion, functionTask.Status);
            Assert.Equal(TaskStatus.RanToCompletion, callTask.Status);
            return functionTask.Result;
        }

        private static IStorageAccount CreateFakeStorageAccount()
        {
            return new FakeStorageAccount();
        }

        private static IStorageQueue CreateQueue(IStorageAccount account, string queueName)
        {
            IStorageQueueClient client = account.CreateQueueClient();
            IStorageQueue queue = client.GetQueueReference(queueName);
            queue.CreateIfNotExists();
            return queue;
        }

        private class BindToCloudQueueMessageProgram
        {
            public static TaskCompletionSource<CloudQueueMessage> TaskSource { get; set; }

            public static void Run([QueueTrigger(QueueName)] CloudQueueMessage message)
            {
                TaskSource.TrySetResult(message);
            }
        }

        private class BindToStringProgram
        {
            public static TaskCompletionSource<string> TaskSource { get; set; }

            public static void Run([QueueTrigger(QueueName)] string message)
            {
                TaskSource.TrySetResult(message);
            }
        }

        private class BindToByteArrayProgram
        {
            public static TaskCompletionSource<byte[]> TaskSource { get; set; }

            public static void Run([QueueTrigger(QueueName)] byte[] message)
            {
                TaskSource.TrySetResult(message);
            }
        }

        private class BindToPocoProgram
        {
            public static TaskCompletionSource<Poco> TaskSource { get; set; }

            public static void Run([QueueTrigger(QueueName)] Poco message)
            {
                TaskSource.TrySetResult(message);
            }
        }

        private class BindToPocoStructProgram
        {
            public static TaskCompletionSource<int> TaskSource { get; set; }

            public static void Run([QueueTrigger(QueueName)] int message)
            {
                TaskSource.TrySetResult(message);
            }
        }

        private class BindToQueueTriggerBindingDataProgram
        {
            public static TaskCompletionSource<string> TaskSource { get; set; }

            public static void Run([QueueTrigger(QueueName)] CloudQueueMessage message, string queueTrigger)
            {
                TaskSource.TrySetResult(queueTrigger);
            }
        }

        private class BindToDequeueCountBindingDataProgram
        {
            public static TaskCompletionSource<int> TaskSource { get; set; }

            public static void Run([QueueTrigger(QueueName)] CloudQueueMessage message, int dequeueCount)
            {
                TaskSource.TrySetResult(dequeueCount);
            }
        }

        private class BindToExpirationTimeBindingDataProgram
        {
            public static TaskCompletionSource<DateTimeOffset> TaskSource { get; set; }

            public static void Run([QueueTrigger(QueueName)] CloudQueueMessage message, DateTimeOffset expirationTime)
            {
                TaskSource.TrySetResult(expirationTime);
            }
        }

        private class BindToIdBindingDataProgram
        {
            public static TaskCompletionSource<string> TaskSource { get; set; }

            public static void Run([QueueTrigger(QueueName)] CloudQueueMessage message, string id)
            {
                TaskSource.TrySetResult(id);
            }
        }

        private class BindToInsertionTimeBindingDataProgram
        {
            public static TaskCompletionSource<DateTimeOffset> TaskSource { get; set; }

            public static void Run([QueueTrigger(QueueName)] CloudQueueMessage message, DateTimeOffset insertionTime)
            {
                TaskSource.TrySetResult(insertionTime);
            }
        }

        private class BindToNextVisibleTimeBindingDataProgram
        {
            public static TaskCompletionSource<DateTimeOffset> TaskSource { get; set; }

            public static void Run([QueueTrigger(QueueName)] CloudQueueMessage message, DateTimeOffset nextVisibleTime)
            {
                TaskSource.TrySetResult(nextVisibleTime);
            }
        }

        private class BindToPopReceiptBindingDataProgram
        {
            public static TaskCompletionSource<string> TaskSource { get; set; }

            public static void Run([QueueTrigger(QueueName)] CloudQueueMessage message, string popReceipt)
            {
                TaskSource.TrySetResult(popReceipt);
            }
        }

        private class BindToPocoStructPropertyBindingDataProgram
        {
            public static TaskCompletionSource<int> TaskSource { get; set; }

            public static void Run([QueueTrigger(QueueName)] Poco message, int int32Value)
            {
                TaskSource.TrySetResult(int32Value);
            }
        }

        private class BindToPocoComplexPropertyBindingDataProgram
        {
            public static TaskCompletionSource<Poco> TaskSource { get; set; }

            public static void Run([QueueTrigger(QueueName)] Poco message, Poco child)
            {
                TaskSource.TrySetResult(child);
            }
        }

        private class Poco
        {
            public string Value { get; set; }

            public int Int32Value { get; set; }

            public Poco Child { get; set; }
        }

        private class PoisonQueueProgram
        {
            public static TaskCompletionSource<string> TaskSource { get; set; }

            public static void PutInPoisonQueue([QueueTrigger(QueueName)] string message)
            {
                throw new InvalidOperationException();
            }

            public static void ReceiveFromPoisonQueue([QueueTrigger(QueueName + "-poison")] string message)
            {
                TaskSource.TrySetResult(message);
            }
        }

        private class MaxDequeueCountProgram
        {
            public static TaskCompletionSource<object> TaskSource { get; set; }

            public static int DequeueCount { get; set; }

            public static void PutInPoisonQueue([QueueTrigger(QueueName)] string message)
            {
                DequeueCount++;
                throw new InvalidOperationException();
            }

            public static void ReceiveFromPoisonQueue([QueueTrigger(QueueName + "-poison")] string message)
            {
                TaskSource.TrySetResult(null);
            }
        }
    }
}
