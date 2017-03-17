// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Listeners;
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
        private const int SBTimeout = 60 * 1000;
        private const string QueueNamePrefix = PrefixForAll + "queue-";
        private const string StartQueueName = QueueNamePrefix + "start";

        private const string TopicName = PrefixForAll + "topic";

        private static EventWaitHandle _topicSubscriptionCalled1;
        private static EventWaitHandle _topicSubscriptionCalled2;

        // These two variables will be checked at the end of the test
        private static string _resultMessage1;
        private static string _resultMessage2;

        private NamespaceManager _namespaceManager;
        private NamespaceManager _secondaryNamespaceManager;
        private ServiceBusConfiguration _serviceBusConfig;
        private RandomNameResolver _nameResolver;
        private string _secondaryConnectionString;

        public ServiceBusEndToEndTests()
        {
            _serviceBusConfig = new ServiceBusConfiguration();
            _nameResolver = new RandomNameResolver();
            _namespaceManager = NamespaceManager.CreateFromConnectionString(_serviceBusConfig.ConnectionString);
            _secondaryConnectionString = AmbientConnectionStringProvider.Instance.GetConnectionString("ServiceBusSecondary");
            _secondaryNamespaceManager = NamespaceManager.CreateFromConnectionString(_secondaryConnectionString);
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
                FunctionListenerException expectedException = null;
                try
                {
                    await ServiceBusEndToEndInternal(typeof(ServiceBusTestJobs_RestrictedAccess));
                }
                catch (FunctionListenerException e)
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
        public async Task ServiceBusEndToEnd_CreatesEntities()
        {
            JobHost host = null;
            var startName = ResolveName(StartQueueName);
            var topicName = ResolveName(TopicName);
            var queueName = ResolveName(QueueNamePrefix);
            try
            {
                host = CreateHost(typeof(ServiceBusTestJobs_EntityCreation));
                await host.StartAsync();
                CreateStartMessage(_serviceBusConfig.ConnectionString, startName);
                CreateStartMessage(_serviceBusConfig.ConnectionString, startName + '1');
                CreateStartMessage(_serviceBusConfig.ConnectionString, startName + '2');

                await TestHelpers.Await(() =>
                {
                    return _namespaceManager.TopicExists(topicName)
                      && _namespaceManager.QueueExists(queueName + '1')
                      && _namespaceManager.QueueExists(queueName + '2');
                }, 30000);

                Assert.Throws<MessagingException>(() => _namespaceManager.QueueExists(topicName));
                Assert.Throws<MessagingException>(() => _namespaceManager.TopicExists(queueName + '1'));
                Assert.Throws<MessagingException>(() => _namespaceManager.TopicExists(queueName + '2'));
            }
            finally
            {
                host?.StopAsync();
                host?.Dispose();
                Cleanup();
                CleanupQueue(startName + '1');
                CleanupQueue(startName + '2');
                CleanupQueue(queueName + '2');
            }
        }

        [Fact]
        public async Task CustomMessageProcessorTest()
        {
            try
            {
                TestTraceWriter trace = new TestTraceWriter(TraceLevel.Info);
                _serviceBusConfig = new ServiceBusConfiguration();
                _serviceBusConfig.MessagingProvider = new CustomMessagingProvider(_serviceBusConfig, trace);

                JobHostConfiguration config = new JobHostConfiguration()
                {
                    NameResolver = _nameResolver,
                    TypeLocator = new FakeTypeLocator(typeof(ServiceBusTestJobs))
                };
                config.Tracing.Tracers.Add(trace);
                config.UseServiceBus(_serviceBusConfig);
                JobHost host = new JobHost(config);

                await ServiceBusEndToEndInternal(typeof(ServiceBusTestJobs), host: host);

                // in addition to verifying that our custom processor was called, we're also
                // verifying here that extensions can log to the TraceWriter
                Assert.Equal(4, trace.Traces.Count(p => p.Message.Contains("Custom processor Begin called!")));
                Assert.Equal(4, trace.Traces.Count(p => p.Message.Contains("Custom processor End called!")));
            }
            finally
            {
                Cleanup();
            }
        }

        [Fact]
        public async Task MultipleAccountTest()
        {
            try
            {
                TestTraceWriter trace = new TestTraceWriter(TraceLevel.Info);
                _serviceBusConfig = new ServiceBusConfiguration();
                _serviceBusConfig.MessagingProvider = new CustomMessagingProvider(_serviceBusConfig, trace);

                JobHostConfiguration config = new JobHostConfiguration()
                {
                    NameResolver = _nameResolver,
                    TypeLocator = new FakeTypeLocator(typeof(ServiceBusTestJobs))
                };
                config.Tracing.Tracers.Add(trace);
                config.UseServiceBus(_serviceBusConfig);
                JobHost host = new JobHost(config);

                string queueName = ResolveName(StartQueueName);
                string queuePrefix = queueName.Replace("-queue-start", "");
                string firstTopicName = string.Format("{0}-topic/Subscriptions/{0}-queue-topic-1", queuePrefix);

                WriteQueueMessage(_secondaryNamespaceManager, _secondaryConnectionString, queueName, "Test");

                _topicSubscriptionCalled1 = new ManualResetEvent(initialState: false);

                await host.StartAsync();

                _topicSubscriptionCalled1.WaitOne(SBTimeout);

                // ensure all logs have had a chance to flush
                await Task.Delay(3000);

                // Wait for the host to terminate
                await host.StopAsync();
                host.Dispose();

                Assert.Equal("Test-topic-1", _resultMessage1);
            }
            finally
            {
                Cleanup();
            }
        }

        private void CleanupQueue(string elementName)
        {
            if (_namespaceManager.QueueExists(elementName))
            {
                _namespaceManager.DeleteQueue(elementName);
            }
        }

        private void Cleanup()
        {
            string elementName = ResolveName(StartQueueName);
            CleanupQueue(elementName);

            if (_secondaryNamespaceManager.QueueExists(elementName))
            {
                _secondaryNamespaceManager.DeleteQueue(elementName);
            }

            elementName = ResolveName(QueueNamePrefix + "1");
            CleanupQueue(elementName);

            elementName = ResolveName(TopicName);
            if (_namespaceManager.TopicExists(elementName))
            {
                _namespaceManager.DeleteTopic(elementName);
            }
        }

        private JobHost CreateHost(Type jobContainerType)
        {
            JobHostConfiguration config = new JobHostConfiguration()
            {
                NameResolver = _nameResolver,
                TypeLocator = new FakeTypeLocator(jobContainerType)
            };
            config.UseServiceBus(_serviceBusConfig);
            return new JobHost(config);
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
                host = CreateHost(jobContainerType);
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

            _topicSubscriptionCalled1.WaitOne(SBTimeout);
            _topicSubscriptionCalled2.WaitOne(SBTimeout);

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
                    string.Format("{0}.MultipleAccounts", jobContainerType.FullName),
                    string.Format("{0}.SBQueue2SBTopic", jobContainerType.FullName),
                    string.Format("{0}.SBTopicListener1", jobContainerType.FullName),
                    string.Format("{0}.SBTopicListener2", jobContainerType.FullName),
                    "Job host started",
                    string.Format("Executing '{0}.SBQueue2SBQueue' (Reason='New ServiceBus message detected on '{1}'.', Id=", jobContainerType.Name, startQueueName),
                    string.Format("Executed '{0}.SBQueue2SBQueue' (Succeeded, Id=", jobContainerType.Name),
                    string.Format("Executing '{0}.SBQueue2SBTopic' (Reason='New ServiceBus message detected on '{1}'.', Id=", jobContainerType.Name, secondQueueName),
                    string.Format("Executed '{0}.SBQueue2SBTopic' (Succeeded, Id=", jobContainerType.Name),
                    string.Format("Executing '{0}.SBTopicListener1' (Reason='New ServiceBus message detected on '{1}'.', Id=", jobContainerType.Name, firstTopicName),
                    string.Format("Executed '{0}.SBTopicListener1' (Succeeded, Id=", jobContainerType.Name),
                    string.Format("Executing '{0}.SBTopicListener2' (Reason='New ServiceBus message detected on '{1}'.', Id=", jobContainerType.Name, secondTopicName),
                    string.Format("Executed '{0}.SBTopicListener2' (Succeeded, Id=", jobContainerType.Name),
                    "Job host stopped"
                }.OrderBy(p => p).ToArray();

                bool hasError = consoleOutputLines.Any(p => p.Contains("Function had errors"));
                if (!hasError)
                {
                    for (int i = 0; i < expectedOutputLines.Length; i++)
                    {
                        Assert.StartsWith(expectedOutputLines[i], consoleOutputLines[i]);
                    }
                }
            }
        }

        private void CreateStartMessage(string serviceBusConnectionString, string queueName)
        {
            WriteQueueMessage(_namespaceManager, serviceBusConnectionString, queueName, "E2E");
        }

        private void WriteQueueMessage(NamespaceManager namespaceManager, string connectionString, string queueName, string message)
        {
            if (!namespaceManager.QueueExists(queueName))
            {
                namespaceManager.CreateQueue(queueName);
            }

            QueueClient queueClient = QueueClient.CreateFromConnectionString(connectionString, queueName);

            using (Stream stream = new MemoryStream())
            using (TextWriter writer = new StreamWriter(stream))
            {
                writer.Write(message);
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
            // Passes service bus message from a queue to another queue
            public static void SBQueue2SBQueue(
                [ServiceBusTrigger(StartQueueName)] string start, int deliveryCount,
                [ServiceBus(QueueNamePrefix + "1")] out string message)
            {
                Assert.Equal(1, deliveryCount);
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

            // Second listener for the topic
            // Just sprinkling Singleton here because previously we had a bug where this didn't work
            // for ServiceBus.
            [Singleton]
            public static void SBTopicListener2(
                [ServiceBusTrigger(TopicName, QueueNamePrefix + "topic-2")] BrokeredMessage message)
            {
                SBTopicListener2Impl(message);
            }

            // Demonstrate triggering on a queue in one account, and writing to a topic
            // in the primary subscription
            public static void MultipleAccounts(
                [ServiceBusTrigger(StartQueueName), ServiceBusAccount("ServiceBusSecondary")] string input,
                [ServiceBus(TopicName)] out string output)
            {
                output = input;
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

        public class ServiceBusTestJobs_EntityCreation : ServiceBusTestJobsBase
        {
            public static void SBQueueTriggerToTopicOutput(
                [ServiceBusTrigger(StartQueueName)] string message,
                [ServiceBus(TopicName, EntityType = EntityType.Topic)] out string output)
            {
                output = "should create topic";
            }

            public static void SBQueueTriggerToDefaultOutput(
                [ServiceBusTrigger(StartQueueName + "1")] string message,
                [ServiceBus(QueueNamePrefix + "1")] out string output)
            {
                output = "should create queue";
            }

            public static void SBQueueTriggerToQueueOutput(
                [ServiceBusTrigger(StartQueueName + "2")] string message,
                [ServiceBus(QueueNamePrefix + "2", EntityType = EntityType.Queue)] out string output)
            {
                output = "should create queue";
            }
        }

        private class CustomMessagingProvider : MessagingProvider
        {
            private readonly ServiceBusConfiguration _config;
            private readonly TraceWriter _trace;

            public CustomMessagingProvider(ServiceBusConfiguration config, TraceWriter trace)
                : base(config)
            {
                _config = config;
                _trace = trace;
            }

            public override MessageProcessor CreateMessageProcessor(string entityPath)
            {
                // demonstrate overriding the default message options
                OnMessageOptions messageOptions = new OnMessageOptions
                {
                    MaxConcurrentCalls = 3,
                    AutoRenewTimeout = TimeSpan.FromMinutes(1)
                };

                return new CustomMessageProcessor(messageOptions, _trace);
            }

            public override MessagingFactory CreateMessagingFactory(string entityPath, string connectionStringName = null)
            {
                // demonstrate that the MessagingFactory can be customized
                // per queue/topic
                string connectionString = GetConnectionString(connectionStringName);
                MessagingFactory factory = MessagingFactory.CreateFromConnectionString(connectionString);
                MessagingFactorySettings settings = factory.GetSettings();
                settings.OperationTimeout = TimeSpan.FromSeconds(15);

                return MessagingFactory.Create(factory.Address, settings);
            }

            public override NamespaceManager CreateNamespaceManager(string connectionStringName = null)
            {
                return base.CreateNamespaceManager(connectionStringName);
            }

            private class CustomMessageProcessor : MessageProcessor
            {
                private readonly TraceWriter _trace;

                public CustomMessageProcessor(OnMessageOptions messageOptions, TraceWriter trace)
                    : base(messageOptions)
                {
                    _trace = trace;
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
