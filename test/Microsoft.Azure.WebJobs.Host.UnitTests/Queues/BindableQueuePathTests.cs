// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Queues
{
    public class BindableQueuePathTests
    {
        [Fact]
        public void Create_IfNonParameterizedPattern_ReturnsBoundPath()
        {
            const string queueNamePattern = "queue-name-with-no-parameters";

            IBindableQueuePath path = BindableQueuePath.Create(queueNamePattern);

            Assert.NotNull(path);
            Assert.Equal(queueNamePattern, path.QueueNamePattern);
            Assert.True(path.IsBound);
        }

        [Fact]
        public void Create_IfParameterizedPattern_ReturnsNotBoundPath()
        {
            const string queueNamePattern = "queue-{name}-with-{parameter}";

            IBindableQueuePath path = BindableQueuePath.Create(queueNamePattern);

            Assert.NotNull(path);
            Assert.Equal(queueNamePattern, path.QueueNamePattern);
            Assert.False(path.IsBound);
        }

        [Fact]
        public void Create_IfNullPattern_Throws()
        {
            ExceptionAssert.ThrowsArgumentNull(() => BindableQueuePath.Create(null), "queueNamePattern");
        }

        [Fact]
        public void Create_IfMalformedPattern_PropagatesThrownException()
        {
            const string queueNamePattern = "malformed-queue-{name%";

            ExceptionAssert.ThrowsFormat(
                () => BindableQueuePath.Create(queueNamePattern), 
                "Invalid template 'malformed-queue-{name%'. Missing closing bracket at position 17.");
        }
    }
}
