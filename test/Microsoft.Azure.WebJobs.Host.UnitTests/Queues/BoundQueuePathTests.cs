// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Queues;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Queues
{
    public class BoundQueuePathTests
    {
        [Fact]
        public void Bind_IfNotNullBindingData_ReturnsResolvedQueueName()
        {
            const string queueNamePattern = "queue-name-with-no-parameters";
            var bindingData = new Dictionary<string, object> { { "name", "value" } };
            IBindableQueuePath path = new BoundQueuePath(queueNamePattern);

            string result = path.Bind(bindingData);

            Assert.Equal(queueNamePattern, result);
        }

        [Fact]
        public void Bind_IfNullBindingData_ReturnsResolvedQueueName()
        {
            const string queueNamePattern = "queue-name-with-no-parameters";
            IBindableQueuePath path = new BoundQueuePath(queueNamePattern);

            string result = path.Bind(null);

            Assert.Equal(queueNamePattern, result);
        }
    }
}
