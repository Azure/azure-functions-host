// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Bindings.Data;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Bindings.Data
{
    public class DataBindingProviderTests
    {
        [Fact]
        public void Create_ReturnsNull_IfByRefParameter()
        {
            // Arrange
            IBindingProvider product = new DataBindingProvider();

            string parameterName = "Parameter";
            Type parameterType = typeof(int).MakeByRefType();
            BindingProviderContext context = CreateBindingContext(parameterName, parameterType);

            // Act
            IBinding binding = product.TryCreateAsync(context).GetAwaiter().GetResult();

            // Assert
            Assert.Null(binding);
        }

        private static BindingProviderContext CreateBindingContext(string parameterName, Type parameterType)
        {
            ParameterInfo parameter = new StubParameterInfo(parameterName, parameterType);
            Dictionary<string, Type> bindingDataContract = new Dictionary<string, Type>
            {
                { parameterName, parameterType }
            };
            BindingProviderContext context = new BindingProviderContext(null, parameter, bindingDataContract,
                CancellationToken.None);
            return context;
        }

        [Fact]
        public void Create_ReturnsNull_IfContainsUnresolvedGenericParameter()
        {
            // Arrange
            IBindingProvider product = new DataBindingProvider();

            string parameterName = "Parameter";
            Type parameterType = typeof(IEnumerable<>);
            BindingProviderContext context = CreateBindingContext(parameterName, parameterType);

            // Act
            IBinding binding = product.TryCreateAsync(context).GetAwaiter().GetResult();

            // Assert
            Assert.Null(binding);
        }

        [Fact]
        public void Create_ReturnsBinding_IfContainsResolvedGenericParameter()
        {
            // Arrange
            IBindingProvider product = new DataBindingProvider();

            string parameterName = "Parameter";
            Type parameterType = typeof(IEnumerable<int>);
            BindingProviderContext context = CreateBindingContext(parameterName, parameterType);

            // Act
            IBinding binding = product.TryCreateAsync(context).GetAwaiter().GetResult();

            // Assert
            Assert.NotNull(binding);
        }

        private class StubParameterInfo : ParameterInfo
        {
            public StubParameterInfo(string name, Type type)
            {
                NameImpl = name;
                ClassImpl = type;
            }
        }
    }
}
