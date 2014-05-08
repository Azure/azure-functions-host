using System;
using System.Collections.Generic;
using Dashboard.Protocols;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Storage.Queue;
using Microsoft.Azure.Jobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace Dashboard.UnitTests.Protocols
{
    public class InvokerTests
    {
        [Fact]
        public void Constructor_IfClientIsNull_Throws()
        {
            // Arrange
            ICloudQueueClient client = null;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => CreateProductUnderTest(client), "client");
        }

        [Fact]
        public void TriggerAndOverride_IfQueueAlreadyExists_AddsExpectedMessage()
        {
            // Arrange
            Guid expectedHostId = CreateGuid();
            string expectedQueueName = QueueNames.GetHostQueueName(expectedHostId);
            TriggerAndOverrideMessage expectedMessage = CreateTriggerMessage();

            List<string> messagesAdded = new List<string>();

            Mock<ICloudQueue> alreadyExistsQueueMock = new Mock<ICloudQueue>();
            alreadyExistsQueueMock
                .Setup(q => q.CreateMessage(It.IsAny<string>()))
                .Returns<string>(CreateMessage);
            alreadyExistsQueueMock
                .Setup(q => q.AddMessage(It.IsAny<ICloudQueueMessage>()))
                .Callback<ICloudQueueMessage>((m) => messagesAdded.Add(m is FakeMessage ? ((FakeMessage)m).Content : null));
            ICloudQueue alreadyExistsQueue = alreadyExistsQueueMock.Object;

            ICloudQueueClient client = CreateFakeClient(expectedQueueName, alreadyExistsQueue);

            IInvoker product = CreateProductUnderTest(client);

            // Act
            product.TriggerAndOverride(expectedHostId, expectedMessage);

            // Assert
            string expectedContent = ToJson(expectedMessage);
            Assert.Equal(new string[] { expectedContent }, messagesAdded.ToArray());
        }

        [Fact]
        public void TriggerAndOverride_IfQueueDoesNotAlreadyExist_AddsExpectedMessage()
        {
            // Arrange
            Guid expectedHostId = CreateGuid();
            string expectedQueueName = QueueNames.GetHostQueueName(expectedHostId);
            TriggerAndOverrideMessage expectedMessage = CreateTriggerMessage();

            List<string> messagesAdded = new List<string>();

            Mock<ICloudQueue> doesNotYetExistQueueMock = new Mock<ICloudQueue>();
            bool exists = false;
            doesNotYetExistQueueMock
                .Setup(q => q.CreateIfNotExists())
                .Callback(() => exists = true);
            doesNotYetExistQueueMock
                .Setup(q => q.CreateMessage(It.IsAny<string>()))
                .Returns<string>(CreateMessage);
            doesNotYetExistQueueMock
                .Setup(q => q.AddMessage(It.IsAny<ICloudQueueMessage>()))
                .Callback<ICloudQueueMessage>((m) =>
                    {
                        if (!exists)
                        {
                            throw CreateStorageException(404);
                        }

                        messagesAdded.Add(m is FakeMessage ? ((FakeMessage)m).Content : null);
                    });
            ICloudQueue doesNotYetExistQueue = doesNotYetExistQueueMock.Object;

            ICloudQueueClient client = CreateFakeClient(expectedQueueName, doesNotYetExistQueue);

            IInvoker product = CreateProductUnderTest(client);

            // Act
            product.TriggerAndOverride(expectedHostId, expectedMessage);

            // Assert
            string expectedContent = ToJson(expectedMessage);
            Assert.Equal(new string[] { expectedContent }, messagesAdded.ToArray());
        }

        [Fact]
        public void TriggerAndOverride_IfMessageIsNull_Throws()
        {
            // Arrange
            Guid hostId = CreateGuid();
            TriggerAndOverrideMessage message = null;
            IInvoker product = CreateProductUnderTest(CreateDummyClient());

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => product.TriggerAndOverride(hostId, message), "message");
        }

        private static IDictionary<string, string> CreateArguments()
        {
            return new Dictionary<string, string>
            {
                { "a", "foo" },
                { "b", "bar" },
                { "c", "baz" }
            };
        }

        private static ICloudQueueClient CreateDummyClient()
        {
            return new Mock<ICloudQueueClient>(MockBehavior.Strict).Object;
        }

        private static ICloudQueueClient CreateFakeClient(string queueName, ICloudQueue queue)
        {
            Mock<ICloudQueueClient> mock = new Mock<ICloudQueueClient>();
            mock.Setup(c => c.GetQueueReference(queueName)).Returns(queue);
            return mock.Object;
        }

        private static string CreateFunctionId()
        {
            return "IgnoreFunctionId";
        }

        private static Guid CreateGuid()
        {
            return Guid.NewGuid();
        }

        private static TriggerAndOverrideMessage CreateTriggerMessage()
        {
            Guid expectedId = CreateGuid();
            string expectedFunctionId = CreateFunctionId();
            IDictionary<string, string> expectedArguments = CreateArguments();

            return new TriggerAndOverrideMessage
            {
                Id = expectedId,
                FunctionId = expectedFunctionId,
                Arguments = expectedArguments
            };
        }

        private static ICloudQueueMessage CreateMessage(string content)
        {
            return new FakeMessage
            {
                Content = content
            };
        }

        private static IInvoker CreateProductUnderTest(ICloudQueueClient client)
        {
            return new Invoker(client);
        }

        private static StorageException CreateStorageException(int httpStatusCode)
        {
            return new StorageException(new RequestResult { HttpStatusCode = httpStatusCode }, null ,null);
        }

        private static string ToJson(object value)
        {
            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            };

            return JsonConvert.SerializeObject(value, Formatting.Indented, settings);
        }

        private class FakeMessage : ICloudQueueMessage
        {
            public string Content { get; set; }
        }
    }
}
