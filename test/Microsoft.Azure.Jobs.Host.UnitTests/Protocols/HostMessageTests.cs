using System;
using Microsoft.Azure.Jobs.Host.Protocols;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Protocols
{
    public class HostMessageTests
    {
        [Fact]
        public void JsonConvert_Roundtrips()
        {
            // Arrange
            HostMessage roundtrip = new HostMessage();

            // Act
            HostMessage message = JsonConvert.DeserializeObject<HostMessage>(
                JsonConvert.SerializeObject(roundtrip));

            // Assert
            Assert.NotNull(message);
            Assert.Equal(typeof(HostMessage).Name, message.Type);
        }

        [Fact]
        public void JsonConvertDerivedType_Roundtrips()
        {
            // Arrange
            TriggerAndOverrideMessage expectedMessage = new TriggerAndOverrideMessage
            {
                Id = Guid.NewGuid()
            };

            // Act
            HostMessage message = JsonConvert.DeserializeObject<HostMessage>(
                JsonConvert.SerializeObject(expectedMessage));

            // Assert
            Assert.NotNull(message);
            Assert.IsType<TriggerAndOverrideMessage>(message);
            TriggerAndOverrideMessage typedMessage = (TriggerAndOverrideMessage)message;
            Assert.Equal(expectedMessage.Id, typedMessage.Id);
        }
    }
}
