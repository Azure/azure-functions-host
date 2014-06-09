using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.TestCommon;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.EndToEndTests
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

        private readonly string _storageConnectionString;
        private readonly string _servicesBusConnectionString;

        private readonly NamespaceManager _namespaceManager;

        private readonly RandomNameResolver _nameResolver = new RandomNameResolver();

        public ServiceBusEndToEndTests()
        {
            _storageConnectionString = ConfigurationManager.ConnectionStrings["AzureStorage"].ConnectionString;
            _servicesBusConnectionString = ConfigurationManager.ConnectionStrings["ServiceBus"].ConnectionString;

            if (string.IsNullOrWhiteSpace(_storageConnectionString) ||
                string.IsNullOrWhiteSpace(_servicesBusConnectionString))
            {
                throw new InvalidOperationException("Cannot run this test because some connection strings are missing from App.config");
            }

            try
            {
                _namespaceManager = NamespaceManager.CreateFromConnectionString(_servicesBusConnectionString);
            }
            catch (Exception ex)
            {
                throw new FormatException("The connection string in App.config is invalid", ex);
            }
        }

        // Passes  service bus message from a queue to another queue
        public static void SBQueue2SBQueue(
            [ServiceBusTrigger(StartQueueName)] string start,
            [ServiceBus(QueueNamePrefix + "1")] out string message)
        {
            message = "SBQueue2SBQueue-" + start;
        }

        // Passes a service bus message from a queue to topic using a brokered message
        public static void SBQueue2SBTopic(
            [ServiceBusTrigger(QueueNamePrefix + "1")] string message,
            [ServiceBus(TopicName)] out BrokeredMessage output)
        {
            message = "SBQueue2SBTopic-" + message;
            output = new BrokeredMessage(message);
        }

        // First listener for the topic
        public static void SBTopicListener1(
            [ServiceBusTrigger(TopicName, QueueNamePrefix + "topic-1")] string message)
        {
            _resultMessage1 = message + "topic-1";
            _topicSubscriptionCalled1.Set();
        }

        // Second listerner for the topic
        public static void SBTopicListener2(
            [ServiceBusTrigger(TopicName, QueueNamePrefix + "topic-2")] BrokeredMessage message)
        {
            _resultMessage2 = message +"topic-2";
            _topicSubscriptionCalled2.Set();
        }

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
            CreateStartMessage();

            JobHostConfiguration config = new JobHostConfiguration(_storageConnectionString)
            {
                ServiceBusConnectionString = _servicesBusConnectionString,
                NameResolver = _nameResolver,
                TypeLocator = new SimpleTypeLocator(this.GetType())
            };

            JobHost host = new JobHost(config);

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            _topicSubscriptionCalled1 = new ManualResetEvent(initialState: false);
            _topicSubscriptionCalled2 = new ManualResetEvent(initialState: false);

            Thread hostThread = new Thread(() => host.RunAndBlock(tokenSource.Token));
            hostThread.Start();

            _topicSubscriptionCalled1.WaitOne();
            _topicSubscriptionCalled2.WaitOne();

            // Stop the host
            tokenSource.Cancel();

            Assert.Equal("SBQueue2SBQueue-SBQueue2SBTopic-topic-1", _resultMessage1);
            Assert.Equal("SBQueue2SBQueue-SBQueue2SBTopic-topic-2", _resultMessage2);
        }

        private void CreateStartMessage()
        {
            string queueName = ResolveName(StartQueueName);
            if (!_namespaceManager.QueueExists(queueName))
            {
                _namespaceManager.CreateQueue(queueName);
            }

            QueueClient queueClient = QueueClient.CreateFromConnectionString(_servicesBusConnectionString, queueName);
            queueClient.Send(new BrokeredMessage("E2E"));
            queueClient.Close();
        }

        // Workaround for the fact that the name resolve only resolves the %%-token
        private string ResolveName(string name)
        {
            return name.Replace("%rnd%", _nameResolver.Resolve("rnd"));
        }
    }
}
