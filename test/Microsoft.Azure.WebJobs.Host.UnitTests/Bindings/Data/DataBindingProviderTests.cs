// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Bindings.Data;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Bindings.Data
{
    public class DataBindingProviderTests
    {
        [Fact]
        public async Task Create_HandlesNullableTypes()
        {
            // Arrange
            IBindingProvider product = new DataBindingProvider();

            string parameterName = "p";
            Type parameterType = typeof(int?);
            BindingProviderContext context = CreateBindingContext(parameterName, parameterType);

            // Act
            IBinding binding = await product.TryCreateAsync(context);

            // Assert
            Assert.NotNull(binding);

            var functionBindingContext = new FunctionBindingContext(Guid.NewGuid(), CancellationToken.None, null);
            var valueBindingContext = new ValueBindingContext(functionBindingContext, CancellationToken.None);
            var bindingData = new Dictionary<string, object>
            {
                { "p", 123 }
            };
            var bindingContext = new BindingContext(valueBindingContext, bindingData);
            var valueProvider = await binding.BindAsync(bindingContext);
            var value = await valueProvider.GetValueAsync();
            Assert.Equal(123, value);

            bindingData["p"] = null;
            bindingContext = new BindingContext(valueBindingContext, bindingData);
            valueProvider = await binding.BindAsync(bindingContext);
            value = await valueProvider.GetValueAsync();
            Assert.Null(value);
        }

        [Fact]
        public async Task Create_NullableTypeMismatch_ThrowsExpectedError()
        {
            // Arrange
            IBindingProvider product = new DataBindingProvider();

            string parameterName = "p";
            Type parameterType = typeof(int?);
            BindingProviderContext context = CreateBindingContext(parameterName, parameterType);

            // Act
            IBinding binding = await product.TryCreateAsync(context);

            // Assert
            Assert.NotNull(binding);

            var functionBindingContext = new FunctionBindingContext(Guid.NewGuid(), CancellationToken.None, null);
            var valueBindingContext = new ValueBindingContext(functionBindingContext, CancellationToken.None);
            var bindingData = new Dictionary<string, object>
            {
                { "p", "123" }
            };
            var bindingContext = new BindingContext(valueBindingContext, bindingData);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await binding.BindAsync(bindingContext);
            });
            Assert.Equal("Binding data for 'p' is not of expected type Nullable<Int32>.", ex.Message);
        }

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
            BindingProviderContext context = new BindingProviderContext(parameter, bindingDataContract,
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
