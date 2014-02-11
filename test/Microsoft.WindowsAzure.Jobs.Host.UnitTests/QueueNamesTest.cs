using System;
using Xunit;

namespace Microsoft.WindowsAzure.Jobs.Host.UnitTests
{
    public class QueueNamesTest
    {
        [Fact]
        public void GetInvokeQueueName_ReturnsExpectedValue()
        {
            // Arrange
            Guid hostId = CreateGuid();

            // Act
            string queueName = QueueNames.GetInvokeQueueName(hostId);

            // Assert
            string expectedQueueName = "azure-jobs-invoke-" + hostId.ToString("N");
            Assert.Equal(expectedQueueName, queueName);
        }

        private static Guid CreateGuid()
        {
            return Guid.NewGuid();
        }
    }
}
