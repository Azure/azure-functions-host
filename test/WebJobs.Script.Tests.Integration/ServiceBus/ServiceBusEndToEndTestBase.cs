// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.ServiceBus
{
    public abstract class ServiceBusEndToEndTestsBase<TTestFixture> :
        EndToEndTestsBase<TTestFixture> where TTestFixture : ServiceBusTestFixture, new()
    {
        public ServiceBusEndToEndTestsBase(TTestFixture fixture) : base(fixture)
        {
        }

        protected async Task ServiceBusQueueTriggerAndOutputTest()
        {
            // ServiceBus tests need the following environment vars:
            // "AzureWebJobsServiceBus" -- the connection string to the account

            var resultBlob = Fixture.TestOutputContainer.GetBlockBlobReference("servicebuse2e-queue-completed");
            await resultBlob.DeleteIfExistsAsync();

            await Fixture.CleanUpEntity(Fixture.TestQueueName1);
            await Fixture.CleanUpEntity(Fixture.TestQueueName2);
            // trigger
            await Fixture.SendQueueMessage(Fixture.TestQueueName1, "test-queue");

            // now wait for function to be invoked
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(resultBlob,
                () => string.Join(Environment.NewLine, Fixture.Host.GetLogMessages()));

            var list = await Fixture.CleanUpEntity(Fixture.TestQueueName2);
            Assert.Equal(list.Count, 1);
            Assert.Equal(list[0], "test-queue-completed");
        }

        protected async Task ServiceBusTopicTriggerTest()
        {
            var resultBlob = Fixture.TestOutputContainer.GetBlockBlobReference("servicebuse2e-topic-completed");
            await resultBlob.DeleteIfExistsAsync();

            await Fixture.CleanUpEntity(Fixture.TestTopicName1);
            // trigger
            await Fixture.SendTopicMessage(Fixture.TestTopicName1, "test-topic");

            // now wait for function to be invoked
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(resultBlob,
                () => string.Join(Environment.NewLine, Fixture.Host.GetLogMessages()));

            Assert.Equal(result, "test-topic-completed");
        }

        protected async Task ServiceBusTopicOutputTest()
        {

            var resultBlob = Fixture.TestOutputContainer.GetBlockBlobReference("servicebuse2e-topic-completed");
            await resultBlob.DeleteIfExistsAsync();

            await Fixture.CleanUpEntity(Fixture.TestTopicName2);
            await Fixture.Host.BeginFunctionAsync("ServiceBusTopicOutput", "test-topic");

            // now wait for function to be invoked
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(resultBlob,
                () => string.Join(Environment.NewLine, Fixture.Host.GetLogMessages()));

            var list = await Fixture.CleanUpEntity(Fixture.TestTopicName2);
            Assert.Equal(list.Count, 1);
            Assert.Equal(list[0], "test-topic-completed");
        }
    }

    public abstract class ServiceBusTestFixture : EndToEndTestFixture
    {
        // Azure Service Bus DotNet Core client does not allow to create new Service Bus entities(queues and topics).
        // So we are using same precreated entities as for WebJobs tests

        public string TestQueueName1 = "core-test-queue1";
        public string TestQueueName2 = "core-test-queue2";
        public string TestTopicName1 = EntityNameHelper.FormatSubscriptionPath("core-test-topic1", "sub1");
        public string TestTopicName2 = EntityNameHelper.FormatSubscriptionPath("core-test-topic1", "sub2");
        private string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.ServiceBus);

        protected ServiceBusTestFixture(string rootPath, string testId) : base(rootPath, testId, "Microsoft.Azure.WebJobs.ServiceBus", "3.0.0-rc*")
        {
        }

        protected override IEnumerable<string> GetActiveFunctions() => new[] { "ServiceBusQueueTriggerAndOutput", "ServiceBusTopicTrigger", "ServiceBusTopicOutput" };

        public async Task<List<string>> CleanUpEntity(string entityPath)
        {
            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.ServiceBus);
            var messageReceiver = new MessageReceiver(connectionString, entityPath, ReceiveMode.ReceiveAndDelete);
            Message message;
            List<string> result = new List<string>();
            do
            {
                message = await messageReceiver.ReceiveAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                if (message != null)
                {
                    result.Add(Encoding.UTF8.GetString(message.Body));
                }
                else
                {
                    break;
                }
            } while (true);
            await messageReceiver.CloseAsync();
            return result;
        }

        public async Task SendQueueMessage(string entityPath, string message)
        {
            var queueClient = new QueueClient(connectionString, entityPath);
            await queueClient.SendAsync(new Message(Encoding.UTF8.GetBytes(message)));
        }

        public async Task SendTopicMessage(string entityPath, string message)
        {
            var topicClient = new TopicClient(connectionString, entityPath);
            await topicClient.SendAsync(new Message(Encoding.UTF8.GetBytes(message)));
        }
    }
}
