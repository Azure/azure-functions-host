// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Bindings.Invoke;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Bindings.Invoke
{
    public class InvokeBindingTests
    {
        [Fact]
        public void Create_ReturnsNull_IfByRefParameter()
        {
            // Arrange
            string parameterName = "Parameter";
            Type parameterType = typeof(int).MakeByRefType();

            // Act
            IBinding binding = InvokeBinding.Create(parameterName, parameterType);

            // Assert
            Assert.Null(binding);
        }

        [Fact]
        public void Create_ReturnsNull_IfContainsUnresolvedGenericParameter()
        {
            // Arrange
            string parameterName = "Parameter";
            Type parameterType = typeof(IEnumerable<>);

            // Act
            IBinding binding = InvokeBinding.Create(parameterName, parameterType);

            // Assert
            Assert.Null(binding);
        }

        [Fact]
        public void Create_ReturnsBinding_IfContainsResolvedGenericParameter()
        {
            // Arrange
            string parameterName = "Parameter";
            Type parameterType = typeof(IEnumerable<int>);

            // Act
            IBinding binding = InvokeBinding.Create(parameterName, parameterType);

            // Assert
            Assert.NotNull(binding);
        }
    }
}
