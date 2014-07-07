using System;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Tables;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Tables
{
    public class PocoEntityArgumentBindingProviderTests
    {
        [Fact]
        public void Create_ReturnsNull_IfByRefParameter()
        {
            // Arrange
            ITableEntityArgumentBindingProvider product = new PocoEntityArgumentBindingProvider();

            Type parameterType = typeof(SimpleTableEntity).MakeByRefType();

            // Act
            IArgumentBinding<TableEntityContext> binding = product.TryCreate(parameterType);

            // Assert
            Assert.Null(binding);
        }

        private class SimpleTableEntity
        {
        }
    }
}
