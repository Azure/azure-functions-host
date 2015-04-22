// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Queues
{
    public class ParameterizedQueuePathTests
    {
        [Fact]
        public void Bind_IfNotNullBindingData_ReturnsResolvedQueueName()
        {
            const string queueNamePattern = "queue-{name}-with-{parameter}";
            var bindingData = new Dictionary<string, object> { { "name", "name" }, { "parameter", "parameter" } };
            IBindableQueuePath path = CreateProductUnderTest(queueNamePattern);

            string result = path.Bind(bindingData);

            Assert.Equal("queue-name-with-parameter", result);
        }

        [Fact]
        public void Bind_IfNullBindingData_Throws()
        {
            const string queueNamePattern = "queue-{name}-with-{parameter}";
            IBindableQueuePath path = CreateProductUnderTest(queueNamePattern);

            ExceptionAssert.ThrowsArgumentNull(() => path.Bind(null), "bindingData");
        }

        private static IBindableQueuePath CreateProductUnderTest(string queueNamePattern)
        {
            BindingTemplate template = BindingTemplate.FromString(queueNamePattern);
            IBindableQueuePath path = new ParameterizedQueuePath(template);
            return path;
        }
    }
}
