// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
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

        private RandomNameResolver _nameResolver;

        // Passes  service bus message from a queue to another queue
        public static void SBQueue2SBQueue(
            [ServiceBusTrigger(StartQueueName)] string start,
            [ServiceBus(QueueNamePrefix + "1")] out string message)
        {
            message = start + "-SBQueue2SBQueue";
        }

        // Passes a service bus message from a queue to topic using a brokered message
        public static void SBQueue2SBTopic(
            [ServiceBusTrigger(QueueNamePrefix + "1")] string message,
            [ServiceBus(TopicName)] out BrokeredMessage output)
        {
            message = message + "-SBQueue2SBTopic";

            Stream stream = new MemoryStream();
            TextWriter writer = new StreamWriter(stream);
            writer.Write(message);
            writer.Flush();
            stream.Position = 0;

            output = new BrokeredMessage(stream)
            {
                ContentType = "text/plain"
            };
        }

        // First listener for the topic
        public static void SBTopicListener1(
            [ServiceBusTrigger(TopicName, QueueNamePrefix + "topic-1")] string message)
        {
            _resultMessage1 = message + "-topic-1";
            _topicSubscriptionCalled1.Set();
        }

        // Second listerner for the topic
        public static void SBTopicListener2(
            [ServiceBusTrigger(TopicName, QueueNamePrefix + "topic-2")] BrokeredMessage message)
        {
            using (Stream stream = message.GetBody<Stream>())
            using (TextReader reader = new StreamReader(stream))
            {
                _resultMessage2 = reader.ReadToEnd() + "-topic-2";
            }

            _topicSubscriptionCalled2.Set();
        }

        [Fact]
        public void ServiceBusEndToEnd()
        {
            try
            {
                ServiceBusEndToEndInternal();
            }
            finally
            {
                // Cleanup
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
        }

        private void ServiceBusEndToEndInternal()
        {
            StringWriter consoleOutput = new StringWriter();
            Console.SetOut(consoleOutput);

            // Reinitialize the name resolver to avoid naming conflicts
            _nameResolver = new RandomNameResolver();

            JobHostConfiguration config = new JobHostConfiguration()
            {
                NameResolver = _nameResolver,
                TypeLocator = new FakeTypeLocator(this.GetType())
            };

            ServiceBusConfiguration serviceBusConfig = new ServiceBusConfiguration();
            config.UseServiceBus(serviceBusConfig);

            _namespaceManager = NamespaceManager.CreateFromConnectionString(serviceBusConfig.ConnectionString);

            string startQueueName = ResolveName(StartQueueName);
            string secondQueueName = startQueueName.Replace("start", "1");
            string queuePrefix = startQueueName.Replace("-queue-start", "");
            string firstTopicName = string.Format("{0}-topic/Subscriptions/{0}-queue-topic-1", queuePrefix);
            string secondTopicName = string.Format("{0}-topic/Subscriptions/{0}-queue-topic-2", queuePrefix);
            CreateStartMessage(serviceBusConfig.ConnectionString, startQueueName);

            JobHost host = new JobHost(config);

            _topicSubscriptionCalled1 = new ManualResetEvent(initialState: false);
            _topicSubscriptionCalled2 = new ManualResetEvent(initialState: false);

            host.Start();

            int timeout = 1 * 60 * 1000;
            _topicSubscriptionCalled1.WaitOne(timeout);
            _topicSubscriptionCalled2.WaitOne(timeout);

            // Wait for the host to terminate
            host.Stop();

            Assert.Equal("E2E-SBQueue2SBQueue-SBQueue2SBTopic-topic-1", _resultMessage1);
            Assert.Equal("E2E-SBQueue2SBQueue-SBQueue2SBTopic-topic-2", _resultMessage2);

            string[] consoleOutputLines = consoleOutput.ToString().Trim().Split(new string[] { Environment.NewLine }, StringSplitOptions.None);
            string[] expectedOutputLines = new string[]
                {
                    "Found the following functions:",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.ServiceBusEndToEndTests.SBQueue2SBQueue",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.ServiceBusEndToEndTests.SBQueue2SBTopic",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.ServiceBusEndToEndTests.SBTopicListener1",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.ServiceBusEndToEndTests.SBTopicListener2",
                    "Job host started",
                    string.Format("Executing: 'ServiceBusEndToEndTests.SBQueue2SBQueue' - Reason: 'New service bus message detected on '{0}.'", startQueueName),
                    string.Format("Executing: 'ServiceBusEndToEndTests.SBQueue2SBTopic' - Reason: 'New service bus message detected on '{0}.'", secondQueueName),
                    string.Format("Executing: 'ServiceBusEndToEndTests.SBTopicListener1' - Reason: 'New service bus message detected on '{0}.'", firstTopicName),
                    string.Format("Executing: 'ServiceBusEndToEndTests.SBTopicListener2' - Reason: 'New service bus message detected on '{0}.'", secondTopicName),
                    "Job host stopped"
                };
            Assert.True(consoleOutputLines.OrderBy(p => p).SequenceEqual(expectedOutputLines.OrderBy(p => p)));
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
    }
}
