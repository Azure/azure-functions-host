// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using Xunit;
using Xunit.Extensions;

namespace Microsoft.Azure.WebJobs.Host.IntegrationTests
{
    public class QueueBindingTests : IDisposable
    {
        internal const string TestQueueName = "myoutputqueue";
        internal const string TestQueueMessage = "Test queue message";
        internal const int TestValue = Int32.MinValue;

        [Fact]
        public void Call_WhenMissingQueue_Creates()
        {
            var account = TestStorage.GetAccount();
            var lc = TestStorage.New<MissingQueueProgram>(account);
            CloudQueue queue = TestQueueClient.GetQueueReference(account, TestQueueName);
            Assert.False(queue.Exists(), "queue should NOT exist before the test");
            
            lc.Call("FuncWithCloudQueue");

            Assert.True(queue.Exists(), "queue must be created");
            AssertQueueEmpty(queue);
        }

        [Theory]
        [InlineData("FuncWithOutCloudQueueMessage", TestQueueMessage)]
        [InlineData("FuncWithOutByteArray", TestQueueMessage)]
        [InlineData("FuncWithOutString", TestQueueMessage)]
        [InlineData("FuncWithICollector", TestQueueMessage)]
        [InlineData("FuncWithOutTNull", "null")]
        public void Call_WhenMissingQueue_CreatesAndSends(string functionName, string expectedMessage)
        {
            var account = TestStorage.GetAccount();
            var lc = TestStorage.New<MissingQueueProgram>(account);
            CloudQueue queue = TestQueueClient.GetQueueReference(account, TestQueueName);
            Assert.False(queue.Exists(), "queue should NOT exist before the test");

            lc.Call(functionName);

            Assert.True(queue.Exists(), "queue must be created");
            AssertMessageSent(queue, expectedMessage);
        }

        [Fact]
        public void CallFuncWithOutT_WhenMissingQueue_CreatesAndSends()
        {
            var account = TestStorage.GetAccount();
            CloudQueue queue = account.CreateCloudQueueClient().GetQueueReference(TestQueueName);
            TestQueueClient.DeleteQueue(queue);
            Assert.False(queue.Exists(), "queue must be deleted before test");
            var lc = TestStorage.New<MissingQueueProgram>(account);

            lc.Call("FuncWithOutT");

            Assert.True(queue.Exists(), "queue must be created");
            AssertMessageSent(queue, new FooPoco { Value = TestValue });
        }

        [Fact]
        public void CallFuncWithOutValueT_WhenMissingQueue_CreatesAndSends()
        {
            var account = TestStorage.GetAccount();
            CloudQueue queue = account.CreateCloudQueueClient().GetQueueReference(TestQueueName);
            TestQueueClient.DeleteQueue(queue);
            Assert.False(queue.Exists(), "queue must be deleted before test");
            var lc = TestStorage.New<MissingQueueProgram>(account);

            lc.Call("FuncWithOutValueT");

            Assert.True(queue.Exists(), "queue must be created");
            AssertMessageSent(queue, new FooValuePoco { Value = TestValue });
        }

        [Theory]
        [InlineData("FuncWithOutCloudQueueMessageNull")]
        [InlineData("FuncWithOutByteArrayNull")]
        [InlineData("FuncWithOutStringNull")]
        [InlineData("FuncWithICollectorNoop")]
        public void Call_WhenMissingQueue_DoesntCreate(string functionName)
        {
            var account = TestStorage.GetAccount();
            var lc = TestStorage.New<MissingQueueProgram>(account);
            CloudQueue queue = TestQueueClient.GetQueueReference(account, TestQueueName);
            Assert.False(queue.Exists(), "queue should NOT exist before the test");

            lc.Call(functionName);

            Assert.False(queue.Exists(), "queue should NOT be created");
        }

        public void Dispose()
        {
            TestQueueClient.DeleteQueue(TestQueueName);
            CloudQueue queue = TestQueueClient.GetQueueReference(TestQueueName);
            Assert.False(queue.Exists(), "queue must be deleted before test");
        }

        private static void AssertQueueEmpty(CloudQueue queue)
        {
            var msg = queue.GetMessage();
            Assert.Null(msg); // no more messages
        }

        private static void AssertMessageSent(CloudQueue queue, string expected)
        {
            var msg = queue.GetMessage();
            Assert.NotNull(msg);
            Assert.Equal(expected, msg.AsString);
        }

        private static void AssertMessageSent<T>(CloudQueue queue, T expected)
        {
            var msg = queue.GetMessage();
            Assert.NotNull(msg);
            T actual = JsonConvert.DeserializeObject<T>(msg.AsString);
            Assert.Equal(expected, actual);
        }

        private class MissingQueueProgram
        {
            public static void FuncWithCloudQueue([Queue(TestQueueName)] CloudQueue queue)
            {
                Assert.NotNull(queue);
                Assert.True(queue.Exists(), "queue must be created by this moment");
            }

            public static void FuncWithOutCloudQueueMessage([Queue(TestQueueName)] out CloudQueueMessage message)
            {
                message = new CloudQueueMessage(TestQueueMessage);
            }

            public static void FuncWithOutCloudQueueMessageNull([Queue(TestQueueName)] out CloudQueueMessage message)
            {
                message = null;
            }

            public static void FuncWithOutByteArray([Queue(TestQueueName)] out byte[] payload)
            {
                payload = Encoding.Default.GetBytes(TestQueueMessage);
            }

            public static void FuncWithOutByteArrayNull([Queue(TestQueueName)] out byte[] payload)
            {
                payload = null;
            }

            public static void FuncWithOutString([Queue(TestQueueName)] out string payload)
            {
                payload = TestQueueMessage;
            }

            public static void FuncWithOutStringNull([Queue(TestQueueName)] out string payload)
            {
                payload = null;
            }

            public static void FuncWithICollector([Queue(TestQueueName)] ICollector<string> queue)
            {
                Assert.NotNull(queue);
                queue.Add(TestQueueMessage);
            }

            public static void FuncWithICollectorNoop([Queue(TestQueueName)] ICollector<FooPoco> queue)
            {
                Assert.NotNull(queue);
            }

            public static void FuncWithOutT([Queue(TestQueueName)] out FooPoco value)
            {
                value = new FooPoco { Value = TestValue };
            }

            public static void FuncWithOutTNull([Queue(TestQueueName)] out FooPoco value)
            {
                value = default(FooPoco);
            }

            public static void FuncWithOutValueT([Queue(TestQueueName)] out FooValuePoco value)
            {
                value = new FooValuePoco { Value = TestValue };
            }
        }

        private class FooPoco
        {
            public int Value { get; set; }
            public string Content { get; set; }

            public override bool Equals(object obj)
            {
                if (obj == null)
                {
                    return false;
                }

                FooPoco other = obj as FooPoco;
                return ((object)other != null) && Value == other.Value && Content == other.Content;
            }

            public override int GetHashCode()
            {
                return Value ^ (Content != null ? Content.GetHashCode() : 0);
            }
        }

        private struct FooValuePoco
        {
            public int Value { get; set; }
            public string Content { get; set; }
        }
    }
}
