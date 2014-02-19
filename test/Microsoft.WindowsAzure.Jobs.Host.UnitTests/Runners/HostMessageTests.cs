using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Jobs.Host.Runners;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.WindowsAzure.Jobs.Host.UnitTests.Runners
{
    public class HostMessageTests
    {
        [Fact]
        public void JsonConvert_Roundtrips()
        {
            // Arrange
            HostMessage expectedMessage = new HostMessage();
            expectedMessage.Type = "Foo";

            // Act
            HostMessage message = JsonConvert.DeserializeObject<HostMessage>(
                JsonConvert.SerializeObject(expectedMessage));

            // Assert
            Assert.NotNull(message);
            Assert.Equal(expectedMessage.Type, message.Type);
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
