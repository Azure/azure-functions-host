using System;
using System.Collections.Generic;
using Microsoft.WindowsAzure.Jobs.Host.Runners;
using Microsoft.WindowsAzure.Jobs.Host.Storage;
using Microsoft.WindowsAzure.Jobs.Host.Storage.Queue;
using Microsoft.WindowsAzure.Jobs.Host.TestCommon;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.WindowsAzure.Jobs.Host.UnitTests.Runners
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
        public void Invoke_IfQueueAlreadyExists_AddsExpectedMessage()
        {
            // Arrange
            Guid expectedHostId = CreateGuid();
            string expectedQueueName = QueueNames.GetInvokeQueueName(expectedHostId);
            InvocationMessage expectedMessage = CreateInvocationMessage();

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
            product.Invoke(expectedHostId, expectedMessage);

            // Assert
            string expectedContent = ToJson(expectedMessage);
            Assert.Equal(new string[] { expectedContent }, messagesAdded.ToArray());
        }

        [Fact]
        public void Invoke_IfQueueDoesNotAlreadyExist_AddsExpectedMessage()
        {
            // Arrange
            Guid expectedHostId = CreateGuid();
            string expectedQueueName = QueueNames.GetInvokeQueueName(expectedHostId);
            InvocationMessage expectedMessage = CreateInvocationMessage();

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
            product.Invoke(expectedHostId, expectedMessage);

            // Assert
            string expectedContent = ToJson(expectedMessage);
            Assert.Equal(new string[] { expectedContent }, messagesAdded.ToArray());
        }

        [Fact]
        public void Invoke_IfMessageIsNull_Throws()
        {
            // Arrange
            Guid hostId = CreateGuid();
            InvocationMessage message = null;
            IInvoker product = CreateProductUnderTest(CreateDummyClient());

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => product.Invoke(hostId, message), "message");
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

        private static InvocationMessage CreateInvocationMessage()
        {
            Guid expectedId = CreateGuid();
            string expectedFunctionId = CreateFunctionId();
            IDictionary<string, string> expectedArguments = CreateArguments();

            return new InvocationMessage
            {
                Type = InvocationMessageType.TriggerAndOverride,
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

        private static CloudStorageException CreateStorageException(int httpStatusCode)
        {
            return new CloudStorageException(new CloudRequestResult(httpStatusCode));
        }

        private static string ToJson(object value)
        {
            return JsonConvert.SerializeObject(value, Formatting.Indented);
        }

        private class FakeMessage : ICloudQueueMessage
        {
            public string Content { get; set; }
        }
    }
}
