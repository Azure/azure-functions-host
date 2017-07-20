// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Executors
{
    public class VoidMethodInvokerTests
    {
        [Fact]
        public void InvokeAsync_DelegatesToLambda()
        {
            // Arrange
            object expectedInstance = new object();
            object[] expectedArguments = new object[0];
            bool invoked = false;
            object instance = null;
            object[] arguments = null;
            Action<object, object[]> lambda = (i, a) =>
            {
                invoked = true;
                instance = i;
                arguments = a;
            };

            IMethodInvoker<object, object> invoker = CreateProductUnderTest(lambda);

            // Act
            Task task = invoker.InvokeAsync(expectedInstance, expectedArguments);
            
            // Assert
            task.GetAwaiter().GetResult();
            Assert.True(invoked);
            Assert.Same(expectedInstance, instance);
            Assert.Same(expectedArguments, arguments);
        }

        private static VoidMethodInvoker<object, object> CreateProductUnderTest(Action<object, object[]> lambda)
        {
            return new VoidMethodInvoker<object, object>(lambda);
        }
    }
}
