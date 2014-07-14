using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Tables;
using Microsoft.WindowsAzure.Storage.Table;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Tables
{
    public class TableEntityValueBinderTests
    {
        [Fact]
        public void HasChanged_ReturnsFalse_IfValueHasNotChanged()
        {
            // Arrange
            TableEntityContext entityContext = new TableEntityContext();
            DynamicTableEntity value = new DynamicTableEntity
            {
                PartitionKey = "PK",
                RowKey = "RK",
                Properties = new Dictionary<string, EntityProperty> { { "Item", new EntityProperty("Foo") } }
            };
            Type valueType = typeof(DynamicTableEntity);
            TableEntityValueBinder product = new TableEntityValueBinder(entityContext, value, valueType);

            // Act
            bool hasChanged = product.HasChanged;

            // Assert
            Assert.False(hasChanged);
        }

        [Fact]
        public void HasChanged_ReturnsTrue_IfPropertyHasBeenAdded()
        {
            // Arrange
            TableEntityContext entityContext = new TableEntityContext();
            DynamicTableEntity value = new DynamicTableEntity
            {
                PartitionKey = "PK",
                RowKey = "RK",
                Properties = new Dictionary<string, EntityProperty> { { "Item", new EntityProperty("Foo") } }
            };
            Type valueType = typeof(DynamicTableEntity);
            TableEntityValueBinder product = new TableEntityValueBinder(entityContext, value, valueType);

            value.Properties["Item2"] = new EntityProperty("Bar");

            // Act
            bool hasChanged = product.HasChanged;

            // Assert
            Assert.True(hasChanged);
        }

        [Fact]
        public void HasChanged_ReturnsTrue_IfValueHasChanged()
        {
            // Arrange
            TableEntityContext entityContext = new TableEntityContext();
            DynamicTableEntity value = new DynamicTableEntity
            {
                PartitionKey = "PK",
                RowKey = "RK",
                Properties = new Dictionary<string, EntityProperty> { { "Item", new EntityProperty("Foo") } }
            };
            Type valueType = typeof(DynamicTableEntity);
            TableEntityValueBinder product = new TableEntityValueBinder(entityContext, value, valueType);

            value.Properties["Item"].StringValue = "Bar";

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
