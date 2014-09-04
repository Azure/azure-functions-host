// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Executors
{
    public class VoidInvokerTests
    {
        [Fact]
        public void InvokeAsync_DelegatesToLambda()
        {
            // Arrange
            object[] expectedArguments = new object[0];
            bool invoked = false;
            object[] arguments = null;
            Action<object[]> lambda = (a) =>
            {
                invoked = true;
                arguments = a;
            };

            IInvoker invoker = CreateProductUnderTest(lambda);

            // Act
            Task task = invoker.InvokeAsync(expectedArguments);
            
            // Assert
            task.GetAwaiter().GetResult();
            Assert.True(invoked);
            Assert.Same(expectedArguments, arguments);
        }

        private static VoidInvoker CreateProductUnderTest(Action<object[]> lambda)
        {
            return new VoidInvoker(new List<string>(), lambda);
        }
    }
}
