// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Tables;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Tables
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
