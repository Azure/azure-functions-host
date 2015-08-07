// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Azure.WebJobs.ServiceBus;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.EndToEndTests
{
    public class ServiceBusEndToEndTests
    {
        private const string PrefixForAll = "t-%rnd%-";

        private const string QueueNamePrefix = PrefixForAll + "queue-";
        private const string StartQueueName = QueueNamePrefix + "start";

        private const string TopicName = PrefixForAll + "topic";

        private static EventWaitHandle _topicSubscriptionCalled1;
        private static EventWaitHandle _topicSubscriptionCalled2;

        // These two variables will be checked at the end of the test
        private static string _resultMessage1;
        private static string _resultMessage2;

        private NamespaceManager _namespaceManager;
        private ServiceBusConfiguration _serviceBusConfig;
        private RandomNameResolver _nameResolver;

        public ServiceBusEndToEndTests()
        {
            _serviceBusConfig = new ServiceBusConfiguration();
            _nameResolver = new RandomNameResolver();
            _namespaceManager = NamespaceManager.CreateFromConnectionString(_serviceBusConfig.ConnectionString);
        }

        [Fact]
        public async Task ServiceBusEndToEnd()
        {
            try
            {
                await ServiceBusEndToEndInternal(typeof(ServiceBusTestJobs));
            }
            finally
            {
                Cleanup();
            }
        }

        [Fact]
        public async Task ServiceBusEndToEnd_RestrictedAccess()
        {
            try
            {
                // Try running the tests using jobs that declare restricted access
                // levels. We expect a failure.
                MessagingEntityNotFoundException expectedException = null;
                try
                {
                    await ServiceBusEndToEndInternal(typeof(ServiceBusTestJobs_RestrictedAccess));
                }
                catch (MessagingEntityNotFoundException e)
                {
                    expectedException = e;
                }
                Assert.NotNull(expectedException);

                // Now create the service bus entities
                string queueName = ResolveName(QueueNamePrefix + "1");
                _namespaceManager.CreateQueue(queueName);

                string topicName = ResolveName(TopicName);
                _namespaceManager.CreateTopic(topicName);

                string subscription1 = ResolveName(QueueNamePrefix + "topic-1");
                _namespaceManager.CreateSubscription(topicName, subscription1);

                string subscription2 = ResolveName(QueueNamePrefix + "topic-2");
                _namespaceManager.CreateSubscription(topicName, subscription2);

                // Test should now succeed
                await ServiceBusEndToEndInternal(typeof(ServiceBusTestJobs_RestrictedAccess), verifyLogs: false);
            }
            finally
            {
                Cleanup();
            }
        }

        [Fact]
        public async Task CustomMessageProcessorTest()
        {
            try
            {
                _serviceBusConfig = new ServiceBusConfiguration
                {
                    MessageProcessorFactory = new CustomMessageProcessorFactory()
                };
                TestTraceWriter trace = new TestTraceWriter(TraceLevel.Info);
                JobHostConfiguration config = new JobHostConfiguration()
                {
                    NameResolver = _nameResolver,
                    TypeLocator = new FakeTypeLocator(typeof(ServiceBusTestJobs)),
                    Trace = trace
                };
                config.UseServiceBus(_serviceBusConfig);
                JobHost host = new JobHost(config);

                await ServiceBusEndToEndInternal(typeof(ServiceBusTestJobs), host: host);

                // in addition to verifying that our custom processor was called, we're also
                // verifying here that extensions can log to the TraceWriter
                Assert.Equal(4, trace.Traces.Count(p => p.Contains("Custom processor Begin called!")));
                Assert.Equal(4, trace.Traces.Count(p => p.Contains("Custom processor End called!")));
            }
            finally
            {
                Cleanup();
            }
        }

        private void Cleanup()
        {
            string elementName = ResolveName(StartQueueName);
            if (_namespaceManager.QueueExists(elementName))
            {
                _namespaceManager.DeleteQueue(elementName);
            }

            elementName = ResolveName(QueueNamePrefix + "1");
            if (_namespaceManager.QueueExists(elementName))
            {
                _namespaceManager.DeleteQueue(elementName);
            }

            elementName = ResolveName(TopicName);
            if (_namespaceManager.TopicExists(elementName))
            {
                _namespaceManager.DeleteTopic(elementName);
            }
        }

        private async Task ServiceBusEndToEndInternal(Type jobContainerType, JobHost host = null, bool verifyLogs = true)
        {
            StringWriter consoleOutput = null;
            TextWriter hold = null;
            if (verifyLogs)
            {
                consoleOutput = new StringWriter();
                hold = Console.Out;
                Console.SetOut(consoleOutput);
            }

            if (host == null)
            {
                JobHostConfiguration config = new JobHostConfiguration()
                {
                    NameResolver = _nameResolver,
                    TypeLocator = new FakeTypeLocator(jobContainerType)
                };
                config.UseServiceBus(_serviceBusConfig);
                host = new JobHost(config);
            }

            string startQueueName = ResolveName(StartQueueName);
            string secondQueueName = startQueueName.Replace("start", "1");
            string queuePrefix = startQueueName.Replace("-queue-start", "");
            string firstTopicName = string.Format("{0}-topic/Subscriptions/{0}-queue-topic-1", queuePrefix);
            string secondTopicName = string.Format("{0}-topic/Subscriptions/{0}-queue-topic-2", queuePrefix);
            CreateStartMessage(_serviceBusConfig.ConnectionString, startQueueName);

            _topicSubscriptionCalled1 = new ManualResetEvent(initialState: false);
            _topicSubscriptionCalled2 = new ManualResetEvent(initialState: false);

            await host.StartAsync();

            int timeout = 1 * 60 * 1000;
            _topicSubscriptionCalled1.WaitOne(timeout);
            _topicSubscriptionCalled2.WaitOne(timeout);

            // ensure all logs have had a chance to flush
            await Task.Delay(3000);

            // Wait for the host to terminate
            await host.StopAsync();
            host.Dispose();

            Assert.Equal("E2E-SBQueue2SBQueue-SBQueue2SBTopic-topic-1", _resultMessage1);
            Assert.Equal("E2E-SBQueue2SBQueue-SBQueue2SBTopic-topic-2", _resultMessage2);

            if (verifyLogs)
            {
                Console.SetOut(hold);

                string[] consoleOutputLines = consoleOutput.ToString().Trim().Split(new string[] { Environment.NewLine }, StringSplitOptions.None).OrderBy(p => p).ToArray();
                string[] expectedOutputLines = new string[]
                {
                    "Found the following functions:",
                    string.Format("{0}.SBQueue2SBQueue", jobContainerType.FullName),
                    string.Format("{0}.SBQueue2SBTopic", jobContainerType.FullName),
                    string.Format("{0}.SBTopicListener1", jobContainerType.FullName),
                    string.Format("{0}.SBTopicListener2", jobContainerType.FullName),
                    "Job host started",
                    string.Format("Executing: '{0}.SBQueue2SBQueue' - Reason: 'New ServiceBus message detected on '{1}'.'", jobContainerType.Name, startQueueName),
                    string.Format("Executed: '{0}.SBQueue2SBQueue' (Succeeded)", jobContainerType.Name),
                    string.Format("Executing: '{0}.SBQueue2SBTopic' - Reason: 'New ServiceBus message detected on '{1}'.'", jobContainerType.Name, secondQueueName),
                    string.Format("Executed: '{0}.SBQueue2SBTopic' (Succeeded)", jobContainerType.Name),
                    string.Format("Executing: '{0}.SBTopicListener1' - Reason: 'New ServiceBus message detected on '{1}'.'", jobContainerType.Name, firstTopicName),
                    string.Format("Executed: '{0}.SBTopicListener1' (Succeeded)", jobContainerType.Name),
                    string.Format("Executing: '{0}.SBTopicListener2' - Reason: 'New ServiceBus message detected on '{1}'.'", jobContainerType.Name, secondTopicName),
                    string.Format("Executed: '{0}.SBTopicListener2' (Succeeded)", jobContainerType.Name),
                    "Job host stopped"
                }.OrderBy(p => p).ToArray();

                Assert.Equal(expectedOutputLines.Length, consoleOutputLines.Length);
                for (int i = 0; i < expectedOutputLines.Length; i++)
                {
                    Assert.Equal(expectedOutputLines[i], consoleOutputLines[i]);
                }
            }
        }

        private void CreateStartMessage(string serviceBusConnectionString, string queueName)
        {
            if (!_namespaceManager.QueueExists(queueName))
            {
                _namespaceManager.CreateQueue(queueName);
            }

            QueueClient queueClient = QueueClient.CreateFromConnectionString(serviceBusConnectionString, queueName);

            using (Stream stream = new MemoryStream())
            using (TextWriter writer = new StreamWriter(stream))
            {
                writer.Write("E2E");
                writer.Flush();
                stream.Position = 0;

                queueClient.Send(new BrokeredMessage(stream) { ContentType = "text/plain" });
            }

            queueClient.Close();
        }

        // Workaround for the fact that the name resolve only resolves the %%-token
        private string ResolveName(string name)
        {
            return name.Replace("%rnd%", _nameResolver.Resolve("rnd"));
        }

        public abstract class ServiceBusTestJobsBase
        {
            protected static string SBQueue2SBQueue_GetOutputMessage(string input)
            {
                return input + "-SBQueue2SBQueue";
            }

            protected static BrokeredMessage SBQueue2SBTopic_GetOutputMessage(string input)
            {
                input = input + "-SBQueue2SBTopic";

                Stream stream = new MemoryStream();
                TextWriter writer = new StreamWriter(stream);
                writer.Write(input);
                writer.Flush();
                stream.Position = 0;

                BrokeredMessage output = new BrokeredMessage(stream)
                {
                    ContentType = "text/plain"
                };

                return output;
            }

            protected static void SBTopicListener1Impl(string input)
            {
                _resultMessage1 = input + "-topic-1";
                _topicSubscriptionCalled1.Set();
            }

            protected static void SBTopicListener2Impl(BrokeredMessage message)
            {
                using (Stream stream = message.GetBody<Stream>())
                using (TextReader reader = new StreamReader(stream))
                {
                    _resultMessage2 = reader.ReadToEnd() + "-topic-2";
                }

                _topicSubscriptionCalled2.Set();
            }
        }

        public class ServiceBusTestJobs : ServiceBusTestJobsBase
        {
            // Passes  service bus message from a queue to another queue
            public static void SBQueue2SBQueue(
                [ServiceBusTrigger(StartQueueName)] string start,
                [ServiceBus(QueueNamePrefix + "1")] out string message)
            {
                message = SBQueue2SBQueue_GetOutputMessage(start);
            }

            // Passes a service bus message from a queue to topic using a brokered message
            public static void SBQueue2SBTopic(
                [ServiceBusTrigger(QueueNamePrefix + "1")] string message,
                [ServiceBus(TopicName)] out BrokeredMessage output)
            {
                output = SBQueue2SBTopic_GetOutputMessage(message);
            }

            // First listener for the topic
            public static void SBTopicListener1(
                [ServiceBusTrigger(TopicName, QueueNamePrefix + "topic-1")] string message)
            {
                SBTopicListener1Impl(message);
            }

            // Second listerner for the topic
            public static void SBTopicListener2(
                [ServiceBusTrigger(TopicName, QueueNamePrefix + "topic-2")] BrokeredMessage message)
            {
                SBTopicListener2Impl(message);
            }
        }

        /// <summary>
        /// This test class declares the same job functions, but with restricted AccessRights.
        /// This means the framework will not create any SB queues/topics/subscriptions if they
        /// don't already exist.
        /// </summary>
        public class ServiceBusTestJobs_RestrictedAccess : ServiceBusTestJobsBase
        {
            // Passes  service bus message from a queue to another queue
            public static void SBQueue2SBQueue(
                [ServiceBusTrigger(StartQueueName, AccessRights.Listen)] string start,
                [ServiceBus(QueueNamePrefix + "1", AccessRights.Send)] out string message)
            {
                message = SBQueue2SBQueue_GetOutputMessage(start);
            }

            // Passes a service bus message from a queue to topic using a brokered message
            public static void SBQueue2SBTopic(
                [ServiceBusTrigger(QueueNamePrefix + "1", AccessRights.Listen)] string message,
                [ServiceBus(TopicName, AccessRights.Send)] out BrokeredMessage output)
            {
                output = SBQueue2SBTopic_GetOutputMessage(message);
            }

            // First listener for the topic
            public static void SBTopicListener1(
                [ServiceBusTrigger(TopicName, QueueNamePrefix + "topic-1", AccessRights.Listen)] string message)
            {
                SBTopicListener1Impl(message);
            }

            // Second listerner for the topic
            public static void SBTopicListener2(
                [ServiceBusTrigger(TopicName, QueueNamePrefix + "topic-2", AccessRights.Listen)] BrokeredMessage message)
            {
                SBTopicListener2Impl(message);
            }
        }

        private class CustomMessageProcessorFactory : IMessageProcessorFactory
        {
            public MessageProcessor Create(MessageProcessorFactoryContext context)
            {
                // demonstrate overriding the defalult messgae options
                context.MessageOptions = new OnMessageOptions
                {
                    MaxConcurrentCalls = 3,
                    AutoRenewTimeout = TimeSpan.FromMinutes(1)
                };

                return new CustomMessageProcessor(context);
            }

            private class CustomMessageProcessor : MessageProcessor
            {
                private readonly TraceWriter _trace;

                public CustomMessageProcessor(MessageProcessorFactoryContext context)
                    : base(context)
                {
                    _trace = context.Trace;
                }

                public override async Task<bool> BeginProcessingMessageAsync(BrokeredMessage message, CancellationToken cancellationToken)
                {
                    _trace.Info("Custom processor Begin called!");
                    return await base.BeginProcessingMessageAsync(message, cancellationToken);
                }

                public override async Task CompleteProcessingMessageAsync(BrokeredMessage message, Executors.FunctionResult result, CancellationToken cancellationToken)
                {
                    _trace.Info("Custom processor End called!");
                    await base.CompleteProcessingMessageAsync(message, result, cancellationToken);
                }
            }
        }
    }
}
