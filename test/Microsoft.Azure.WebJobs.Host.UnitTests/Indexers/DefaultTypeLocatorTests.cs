// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Indexers
{
    public class DefaultTypeLocatorTests
    {
        [Fact]
        public void IsJobClass_IfNull_ReturnsFalse()
        {
            Type type = null;

            // Act
            bool result = DefaultTypeLocator.IsJobClass(type);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsJobClass_IfValueType_ReturnsFalse()
        {
            Type type = typeof(PublicStruct);

            // Act
            bool result = DefaultTypeLocator.IsJobClass(type);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsJobClass_IfInterface_ReturnsFalse()
        {
            Type type = typeof(PublicInterface);

            // Act
            bool result = DefaultTypeLocator.IsJobClass(type);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsJobClass_IfAbstract_ReturnsFalse()
        {
            Type type = typeof(AbstractClass);

            // Act
            bool result = DefaultTypeLocator.IsJobClass(type);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsJobClass_IfPrivate_ReturnsFalse()
        {
            Type type = typeof(PrivateClass);

            // Act
            bool result = DefaultTypeLocator.IsJobClass(type);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsJobClass_IfNestedPublic_ReturnsFalse()
        {
            Type type = typeof(NestedPublicClass);

            // Act
            bool result = DefaultTypeLocator.IsJobClass(type);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsJobClass_IfContainsGenericParameters_ReturnsFalse()
        {
            Type type = typeof(GenericClass<>);

            // Act
            bool result = DefaultTypeLocator.IsJobClass(type);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsJobClass_IfNormalPublicClass_ReturnsTrue()
        {
            Type type = typeof(PublicClass);

            // Act
            bool result = DefaultTypeLocator.IsJobClass(type);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void IsJobClass_IfNormalStaticClass_ReturnsTrue()
        {
            Type type = typeof(StaticClass);

            // Act
            bool result = DefaultTypeLocator.IsJobClass(type);

            // Assert
            Assert.True(result);
        }

        public class NestedPublicClass { }

        private class PrivateClass { }
    }

    public abstract class AbstractClass { }

    public struct PublicStruct { }

    public interface PublicInterface { }

    public class GenericClass<T> { }

    public class PublicClass { }

    public static class StaticClass { }
}
