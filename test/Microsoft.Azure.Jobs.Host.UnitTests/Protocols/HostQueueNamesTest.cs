// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
