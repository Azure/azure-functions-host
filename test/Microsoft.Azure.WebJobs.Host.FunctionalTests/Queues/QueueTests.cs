// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Queue;
using Xunit;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Newtonsoft.Json;
using System.Reflection;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    public class QueueTests
    {
        private const string TriggerQueueName = "input";
        private const string QueueName = "output";

        // Test binding to generics. 
        public class GenericProgram<T>
        {
            public void Func([Queue(QueueName)] T q)
            {
                var x = (ICollector<string>) q;
                x.Add("123");                
            }
        }

        [Fact]
        public void TestGenericSucceeds()
        {
            IStorageAccount account = CreateFakeStorageAccount();          
            var host = TestHelpers.NewJobHost<GenericProgram<ICollector<string>>>(account);
            
            host.Call("Func");

            // Now peek at messages. 
            var queue = account.CreateQueueClient().GetQueueReference(QueueName);
            var msgs = queue.GetMessages(10).ToArray();

            Assert.Equal(1, msgs.Length);
            Assert.Equal("123", msgs[0].AsString);
        }

        // Program with a static bad queue name (no { } ). 
        // Use this to test queue name validation. 
        public class ProgramWithStaticBadName
        {
            public const string BadQueueName = "test*"; // Don't include any { }

            // Queue paths without any { } are eagerly validated at indexing time.
            public void Func([Queue(BadQueueName)] ICollector<string> q)
            {
            }
        }

        [Fact]
        public void Catch_Bad_Name_At_IndexTime()
        {
            IStorageAccount account = CreateFakeStorageAccount();
            var host = TestHelpers.NewJobHost<ProgramWithStaticBadName>();

            string errorMessage = GetErrorMessageForBadQueueName(ProgramWithStaticBadName.BadQueueName, "name");

            TestHelpers.AssertIndexingError(() => host.Call("Func"), "ProgramWithStaticBadName.Func", errorMessage);
        }

        private static string GetErrorMessageForBadQueueName(string value, string parameterName)
        {
            return "A queue name can contain only letters, numbers, and and dash(-) characters - \"" + value+ "\"" +
                "\r\nParameter name: " + parameterName; // from ArgumentException 
        }

        // Program with variable queue name containing both %% and { }.
        // Has valid parameter binding.   Use this to test queue name validation at various stages. 
        public class ProgramWithVariableQueueName
        {
            public const string QueueNamePattern = "q%key%-test{x}";

            // Queue paths without any { } are eagerly validated at indexing time.
            public void Func([Queue(QueueNamePattern)] ICollector<string> q)
            {
            }
        }

        [Fact]
        public void Catch_Bad_Name_At_Runtime()
        {
            var nameResolver = new FakeNameResolver().Add("key", "1");
            IStorageAccount account = CreateFakeStorageAccount();
            var host = TestHelpers.NewJobHost<ProgramWithVariableQueueName>(account, nameResolver);

            host.Call("Func", new { x = "1" }); // succeeds with valid char

            try
            {
                host.Call("Func", new { x = "*" }); // produces an error pattern. 
                Assert.False(true, "should have failed");
            }
            catch (FunctionInvocationException e)
            {
                Assert.Equal("Exception binding parameter 'q'", e.InnerException.Message);

                string errorMessage = GetErrorMessageForBadQueueName("q1-test*", "name");
                Assert.Equal(errorMessage, e.InnerException.InnerException.Message);
            }
        }

        // The presence of { } defers validation until runtime. Even if there are illegal chars known at index time! 
        [Fact]
        public void Catch_Bad_Name_At_Runtime_With_Illegal_Static_Chars()
        {
            var nameResolver = new FakeNameResolver().Add("key", "$"); // Illegal
            IStorageAccount account = CreateFakeStorageAccount();
            var host = TestHelpers.NewJobHost<ProgramWithVariableQueueName>(account, nameResolver);

            try
            {
                host.Call("Func", new { x = "1" }); // produces an error pattern. 
                Assert.False(true, "should have failed");
            }
            catch (FunctionInvocationException e) // Not an index exception!
            {
                Assert.Equal("Exception binding parameter 'q'", e.InnerException.Message);

                string errorMessage = GetErrorMessageForBadQueueName("q$-test1", "name");
                Assert.Equal(errorMessage, e.InnerException.InnerException.Message);
            }
        }

        public class ProgramWithTriggerAndBindingData
        {
            public class Poco
            {
                public string xyz { get; set; }
            }

            // BindingData is case insensitive. 
            // And queue name is normalized to lowercase. 
            public const string QueueOutName = "qName-{XYZ}";
            public void Func([QueueTrigger(QueueName)] Poco triggers,  [Queue(QueueOutName)] ICollector<string> q)
            {
                q.Add("123");
            }        
        }

        [Fact]
        public void InvokeWithBindingData()
        {
            // Verify that queue binding pattern has uppercase letters in it. These get normalized to lowercase.
            Assert.NotEqual(ProgramWithTriggerAndBindingData.QueueOutName, ProgramWithTriggerAndBindingData.QueueOutName.ToLower());

            IStorageAccount account = CreateFakeStorageAccount();
            var host = TestHelpers.NewJobHost<ProgramWithTriggerAndBindingData>(account);

            var trigger = new ProgramWithTriggerAndBindingData.Poco { xyz = "abc" };
            host.Call("Func", new
            {
                triggers = new CloudQueueMessage(JsonConvert.SerializeObject(trigger))
            });

            // Now peek at messages. 
            // queue name is normalized to lowercase. 
            var queue = account.CreateQueueClient().GetQueueReference("qname-abc");
            var msgs = queue.GetMessages(10).ToArray();

            Assert.Equal(1, msgs.Length);
            Assert.Equal("123", msgs[0].AsString);
        }


        public class ProgramSimple
        {
            public void Func([Queue(QueueName)] out string x)
            {
                x = "abc";
            }
        }

        // Nice failure when no storage account is set
        [Fact]
        public void Fails_When_No_Storage_is_set()
        {
            var host = TestHelpers.NewJobHost<ProgramSimple>();  // no storage account!

            TestHelpers.AssertIndexingError(() => host.Call("Func"),
                "ProgramSimple.Func", "Unable to bind Queue because no storage account has been configured.");
        }

        public class ProgramBadContract
        {
            public void Func([QueueTrigger(QueueName)] string triggers, [Queue("queuName-{xyz}")] ICollector<string> q)
            {
            }
        }

        [Fact]
        public void Fails_BindingContract_Mismatch()
        {
            // Verify that indexing fails if the [Queue] trigger needs binding data that's not present. 
            IStorageAccount account = CreateFakeStorageAccount();
            var host = TestHelpers.NewJobHost<ProgramBadContract>(account);

            TestHelpers.AssertIndexingError(() => host.Call("Func"),
                "ProgramBadContract.Func",
                "No binding parameter exists for 'xyz'.");
        }

        public class ProgramCantBindToObject
        {
            public void Func([Queue(QueueName)] out object o)
            {
                o = null;
            }
        }

        [Fact]
        public void Fails_Cant_Bind_To_Object()
        {
            IStorageAccount account = CreateFakeStorageAccount();
            var host = TestHelpers.NewJobHost<ProgramCantBindToObject>(account);

            TestHelpers.AssertIndexingError(() => host.Call("Func"),
                "ProgramCantBindToObject.Func",
                "Object element types are not supported.");
        }
        
        [Theory]
        [InlineData(typeof(int), "System.Int32")]
        [InlineData(typeof(DateTime), "System.DateTime")]
        [InlineData(typeof(IEnumerable<string>), "System.Collections.Generic.IEnumerable`1[System.String]")] // Should use ICollector<string> instead
        public void Fails_Cant_Bind_To_Types(Type typeParam, string typeName)
        {
            var m = this.GetType().GetMethod("Fails_Cant_Bind_To_Types_Worker", BindingFlags.Instance | BindingFlags.NonPublic);
            var m2 = m.MakeGenericMethod(typeParam);
            try
            {
                m2.Invoke(this, new object[] { typeName });
            }
            catch (TargetException e)
            {
                throw e.InnerException;
            }
        }
            
        private void Fails_Cant_Bind_To_Types_Worker<T>(string typeName)
        {
            IStorageAccount account = CreateFakeStorageAccount();
            var host = TestHelpers.NewJobHost<GenericProgram<T>>(account);

            TestHelpers.AssertIndexingError(() => host.Call("Func"),
                "GenericProgram`1.Func",
                "Can't bind Queue to type '" + typeName + "'.");
        }

        [Fact]
        public void Queue_IfBoundToCloudQueue_BindsAndCreatesQueue()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueueClient client = account.CreateQueueClient();
            IStorageQueue triggerQueue = CreateQueue(client, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage("ignore"));

            // Act
            CloudQueue result = RunTrigger<CloudQueue>(account, typeof(BindToCloudQueueProgram),
                (s) => BindToCloudQueueProgram.TaskSource = s);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(QueueName, result.Name);
            IStorageQueue queue = client.GetQueueReference(QueueName);
            Assert.True(queue.Exists());
        }

        [Fact]
        public void Queue_IfBoundToICollectorCloudQueueMessage_AddEnqueuesMessage()
        {
            // Arrange
            string expectedContent = Guid.NewGuid().ToString();
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueueClient client = account.CreateQueueClient();
            IStorageQueue triggerQueue = CreateQueue(client, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage(expectedContent));

            // Act
            RunTrigger<object>(account, typeof(BindToICollectorCloudQueueMessageProgram),
                (s) => BindToICollectorCloudQueueMessageProgram.TaskSource = s);

            // Assert
            IStorageQueue queue = client.GetQueueReference(QueueName);
            IEnumerable<IStorageQueueMessage> messages = queue.GetMessages(messageCount: 10);
            Assert.NotNull(messages);
            Assert.Equal(1, messages.Count());
            IStorageQueueMessage message = messages.Single();
            Assert.Equal(expectedContent, message.AsString);
        }

        private static IStorageAccount CreateFakeStorageAccount()
        {
            return new FakeStorageAccount();
        }

        private static IStorageQueue CreateQueue(IStorageQueueClient client, string queueName)
        {
            IStorageQueue queue = client.GetQueueReference(queueName);
            queue.CreateIfNotExists();
            return queue;
        }

        private static TResult RunTrigger<TResult>(IStorageAccount account, Type programType,
            Action<TaskCompletionSource<TResult>> setTaskSource)
        {
            return FunctionalTest.RunTrigger<TResult>(account, programType, setTaskSource);
        }

        private class BindToCloudQueueProgram
        {
            public static TaskCompletionSource<CloudQueue> TaskSource { get; set; }

            public static void Run([QueueTrigger(TriggerQueueName)] CloudQueueMessage ignore,
                [Queue(QueueName)] CloudQueue queue)
            {
                TaskSource.TrySetResult(queue);
            }
        }

        private class BindToICollectorCloudQueueMessageProgram
        {
            public static TaskCompletionSource<object> TaskSource { get; set; }

            public static void Run([QueueTrigger(TriggerQueueName)] CloudQueueMessage message,
                [Queue(QueueName)] ICollector<CloudQueueMessage> queue)
            {
                queue.Add(message);
                TaskSource.TrySetResult(null);
            }
        }
    }
}
