using System;
using Xunit;

namespace Microsoft.WindowsAzure.Jobs.Host.UnitTests
{
    public class QueueNamesTest
    {
        [Fact]
        public void GetHostQueueName_ReturnsExpectedValue()
        {
            // Arrange
            Guid hostId = CreateGuid();

            // Act
            string queueName = QueueNames.GetHostQueueName(hostId);

            // Assert
            string expectedQueueName = "azure-jobs-host-" + hostId.ToString("N");
            Assert.Equal(expectedQueueName, queueName);
        }

        private static Guid CreateGuid()
        {
            return Guid.NewGuid();
        }
    }
}
