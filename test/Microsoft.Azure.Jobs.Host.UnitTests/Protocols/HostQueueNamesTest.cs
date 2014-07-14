using System;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Protocols
{
    public class HostQueueNamesTest
    {
        [Fact]
        public void GetHostQueueName_ReturnsExpectedValue()
        {
            // Arrange
            Guid hostId = CreateGuid();

            // Act
            string queueName = HostQueueNames.GetHostQueueName(hostId);

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
