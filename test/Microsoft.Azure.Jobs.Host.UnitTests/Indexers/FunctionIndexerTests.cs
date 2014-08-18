// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Indexers;
using Microsoft.Azure.Jobs.Host.Protocols;
using Moq;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Indexers
{
    public class FunctionIndexerTests
    {
        [Fact]
        public void IndexMethod_IgnoresMethod_IfMethodHasUnboundOutParameterWithoutJobsAttribute()
        {
            // Arrange
            Mock<IFunctionIndex> indexMock = new Mock<IFunctionIndex>(MockBehavior.Strict);
            int calls = 0;
            indexMock
                .Setup(i => i.Add(It.IsAny<IFunctionDefinition>(), It.IsAny<FunctionDescriptor>(), It.IsAny<MethodInfo>()))
                .Callback(() => calls++);
            IFunctionIndex index = indexMock.Object;
            FunctionIndexer product = CreateProductUnderTest();

            // Act
            product.IndexMethodAsync(typeof(FunctionIndexerTests).GetMethod("TryParse"), index,
                CancellationToken.None).GetAwaiter().GetResult();

            // Assert
            Assert.Equal(0, calls);
        }

        [Fact]
        public void IndexMethod_Throws_IfMethodHasUnboundOutParameterWithJobsAttribute()
        {
            // Arrange
            Mock<IFunctionIndex> indexMock = new Mock<IFunctionIndex>(MockBehavior.Strict);
            int calls = 0;
            indexMock
                .Setup(i => i.Add(It.IsAny<IFunctionDefinition>(), It.IsAny<FunctionDescriptor>(), It.IsAny<MethodInfo>()))
                .Callback(() => calls++);
            IFunctionIndex index = indexMock.Object;
            FunctionIndexer product = CreateProductUnderTest();

            // Act & Assert
            FunctionIndexingException exception = Assert.Throws<FunctionIndexingException>(
                () => product.IndexMethodAsync(typeof(FunctionIndexerTests).GetMethod("FailIndexing"), index,
                    CancellationToken.None).GetAwaiter().GetResult());
            InvalidOperationException innerException = exception.InnerException as InvalidOperationException;
            Assert.NotNull(innerException);
            Assert.Equal("Cannot bind parameter 'parsed' to type Foo&.", innerException.Message);
        }

        [Fact]
        public void IndexMethod_IgnoresMethod_IfMethodHasUnresolvedGenericParameter()
        {
            // Arrange
            Mock<IFunctionIndex> indexMock = new Mock<IFunctionIndex>();
            FunctionIndexer product = CreateProductUnderTest();

            // Act
            product.IndexMethodAsync(typeof(FunctionIndexerTests).GetMethod("MethodWithGenericParameter"), indexMock.Object, CancellationToken.None).GetAwaiter().GetResult();

            // Verify
            indexMock.Verify(i => i.Add(It.IsAny<IFunctionDefinition>(), It.IsAny<FunctionDescriptor>(), It.IsAny<MethodInfo>()), Times.Never);
        }

        [Fact]
        public void IsAzureJobsMethod_ReturnsFalse_IfMethodHasUnresolvedGenericParameter()
        {
            // Arrange
            Mock<IFunctionIndex> indexMock = new Mock<IFunctionIndex>();
            FunctionIndexer product = CreateProductUnderTest();

            // Act
            bool actual = product.IsAzureJobsMethod(typeof(FunctionIndexerTests).GetMethod("MethodWithGenericParameter"));

            // Verify
            Assert.Equal(false, actual);
        }

        [Fact]
        public void IsAzureJobsMethod_ReturnsFalse_IfMethodHasNoParameters()
        {
            // Arrange
            Mock<IFunctionIndex> indexMock = new Mock<IFunctionIndex>();
            FunctionIndexer product = CreateProductUnderTest();

            // Act
            bool actual = product.IsAzureJobsMethod(typeof(FunctionIndexerTests).GetMethod("MethodWithNoParameters"));

            // Verify
            Assert.Equal(false, actual);
        }

        [Fact]
        public void IsAzureJobsMethod_ReturnsTrue_IfMethodHasAzureJobsAttribute()
        {
            // Arrange
            Mock<IFunctionIndex> indexMock = new Mock<IFunctionIndex>();
            FunctionIndexer product = CreateProductUnderTest();

            // Act
            bool actual = product.IsAzureJobsMethod(typeof(FunctionIndexerTests).GetMethod("MethodWithAzureJobsAttribute"));

            // Verify
            Assert.Equal(true, actual);
        }

        [Fact]
        public void IsAzureJobsMethod_ReturnsTrue_IfMethodHasAzureJobsParameterAttributes()
        {
            // Arrange
            Mock<IFunctionIndex> indexMock = new Mock<IFunctionIndex>();
            FunctionIndexer product = CreateProductUnderTest();

            // Act
            bool actual = product.IsAzureJobsMethod(typeof(FunctionIndexerTests).GetMethod("MethodWithAzureJobsParameterAttributes"));

            // Verify
            Assert.Equal(true, actual);
        }

        [Fact]
        public void IsAzureJobsMethod_ReturnsFalse_IfMethodHasNoAzureJobsAttributes()
        {
            // Arrange
            Mock<IFunctionIndex> indexMock = new Mock<IFunctionIndex>();
            FunctionIndexer product = CreateProductUnderTest();

            // Act
            bool actual = product.IsAzureJobsMethod(typeof(FunctionIndexerTests).GetMethod("TryParse"));

            // Verify
            Assert.Equal(false, actual);
        }

        private static FunctionIndexer CreateProductUnderTest()
        {
            FunctionIndexerContext context = FunctionIndexerContext.CreateDefault(null, null, null, null);
            return new FunctionIndexer(context);
        }

        [NoAutomaticTrigger]
        public static bool FailIndexing(string input, out Foo parsed)
        {
            throw new NotImplementedException();
        }

        public static bool TryParse(string input, out Foo parsed)
        {
            throw new NotImplementedException();
        }

        public class Foo
        {
        }

        public static IEnumerable<IEnumerable<T>> MethodWithGenericParameter<T>(IEnumerable<T> source)
        {
            throw new NotImplementedException();
        }

        public static bool MethodWithNoParameters()
        {
            throw new NotImplementedException();
        }

        [NoAutomaticTrigger]
        public static bool MethodWithAzureJobsAttribute(string input, out string output)
        {
            throw new NotImplementedException();
        }

        public static void MethodWithAzureJobsParameterAttributes([QueueTrigger("queue")] string input, [Blob("container/output")] TextWriter writer)
        {
            throw new NotImplementedException();
        }
    }
}
