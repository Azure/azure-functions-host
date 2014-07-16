// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Bindings.Data;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Bindings.Data
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
            ParameterInfo parameter = new StubParameterInfo(parameterName, parameterType);
            Dictionary<string, Type> bindingDataContract = new Dictionary<string, Type>
            {
                { parameterName, parameterType } 
            };
            BindingProviderContext context =
                new BindingProviderContext(null, null, null, parameter, bindingDataContract);

            // Act
            IBinding binding = product.TryCreate(context);

            // Assert
            Assert.Null(binding);
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
