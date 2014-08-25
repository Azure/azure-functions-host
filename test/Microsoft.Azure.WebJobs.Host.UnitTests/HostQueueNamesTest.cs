// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    public class HostQueueNamesTest
    {
        [Fact]
        public void BlobTriggerPoisonQueue_IsExpectedValue()
        {
            // Act
            string queueName = HostQueueNames.BlobTriggerPoisonQueue;

            // Assert
            Assert.Equal("webjobs-blobtrigger-poison", queueName);
        }

        [Fact]
        public void GetHostBlobTriggerQueueName_ReturnsExpectedValue()
        {
            // Arrange
            Guid hostId = CreateGuid();

            // Act
            string queueName = HostQueueNames.GetHostBlobTriggerQueueName(hostId);

            // Assert
            string expectedQueueName = "azure-webjobs-blobtrigger-" + hostId.ToString("N");
            Assert.Equal(expectedQueueName, queueName);
        }

        [Fact]
        public void GetHostQueueName_ReturnsExpectedValue()
        {
            // Arrange
            Guid hostId = CreateGuid();

            // Act
            string queueName = HostQueueNames.GetHostQueueName(hostId);

            // Assert
            string expectedQueueName = "azure-webjobs-host-" + hostId.ToString("N");
            Assert.Equal(expectedQueueName, queueName);
        }

        private static Guid CreateGuid()
        {
            return Guid.NewGuid();
        }
    }
}
