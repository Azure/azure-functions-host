using System;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Tables;
using Microsoft.WindowsAzure.Storage.Table;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Tables
{
    public class TableEntityArgumentBindingProviderTests
    {
        [Fact]
        public void Create_ReturnsNull_IfByRefParameter()
        {
            // Arrange
            ITableEntityArgumentBindingProvider product = new TableEntityArgumentBindingProvider();

            Type parameterType = typeof(SimpleTableEntity).MakeByRefType();

            // Act
            IArgumentBinding<TableEntityContext> binding = product.TryCreate(parameterType);

            // Assert
            Assert.Null(binding);
        }

        private class SimpleTableEntity : TableEntity
        {
        }
    }
}
