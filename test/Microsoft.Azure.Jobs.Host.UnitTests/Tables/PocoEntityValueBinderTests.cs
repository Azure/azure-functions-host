using System;
using Microsoft.Azure.Jobs.Host.Tables;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Tables
{
    public class PocoEntityValueBinderTests
    {
        [Fact]
        public void HasChanged_ReturnsFalse_IfValueHasNotChanged()
        {
            // Arrange
            TableEntityContext entityContext = new TableEntityContext();
            SimpleTableEntity value = new SimpleTableEntity { Item = "Foo" };
            Type valueType = typeof(SimpleTableEntity);
            PocoEntityValueBinder product = new PocoEntityValueBinder(entityContext, value, valueType);

            // Act
            bool hasChanged = product.HasChanged;

            // Assert
            Assert.False(hasChanged);
        }

        [Fact]
        public void HasChanged_ReturnsTrue_IfValueHasChanged()
        {
            // Arrange
            TableEntityContext entityContext = new TableEntityContext();
            SimpleTableEntity value = new SimpleTableEntity { Item = "Foo" };
            Type valueType = typeof(SimpleTableEntity);
            PocoEntityValueBinder product = new PocoEntityValueBinder(entityContext, value, valueType);

            value.Item = "Bar";

            // Act
            bool hasChanged = product.HasChanged;

            // Assert
            Assert.True(hasChanged);
        }

        private class SimpleTableEntity
        {
            public string Item { get; set; }
        }
    }
}
