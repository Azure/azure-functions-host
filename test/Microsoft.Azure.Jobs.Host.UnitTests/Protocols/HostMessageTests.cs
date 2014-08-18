// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Protocols
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
            CallAndOverrideMessage expectedMessage = new CallAndOverrideMessage
            {
                Id = Guid.NewGuid()
            };

            // Act
            HostMessage message = JsonConvert.DeserializeObject<HostMessage>(
                JsonConvert.SerializeObject(expectedMessage));

            // Assert
            Assert.NotNull(message);
            Assert.IsType<CallAndOverrideMessage>(message);
            CallAndOverrideMessage typedMessage = (CallAndOverrideMessage)message;
            Assert.Equal(expectedMessage.Id, typedMessage.Id);
        }
    }
}
