// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Moq;
using Xunit;
using Xunit.Extensions;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Indexers
{
    public class FunctionIndexerTests
    {
        [Fact]
        public void IndexMethod_Throws_IfMethodHasUnboundOutParameterWithJobsAttribute()
        {
            // Arrange
            Mock<IFunctionIndexCollector> indexMock = new Mock<IFunctionIndexCollector>(MockBehavior.Strict);
            int calls = 0;
            indexMock
                .Setup(i => i.Add(It.IsAny<IFunctionDefinition>(), It.IsAny<FunctionDescriptor>(), It.IsAny<MethodInfo>()))
                .Callback(() => calls++);
            IFunctionIndexCollector index = indexMock.Object;
            FunctionIndexer product = CreateProductUnderTest();

            // Act & Assert
            FunctionIndexingException exception = Assert.Throws<FunctionIndexingException>(
                () => product.IndexMethodAsync(typeof(FunctionIndexerTests).GetMethod("FailIndexing"), index,
                    CancellationToken.None).GetAwaiter().GetResult());
            InvalidOperationException innerException = exception.InnerException as InvalidOperationException;
            Assert.NotNull(innerException);
            Assert.Equal("Cannot bind parameter 'parsed' to type Foo&.", innerException.Message);
        }

        [Theory]
        [InlineData("MethodWithUnboundOutParameterAndNoSdkAttribute")]
        [InlineData("MethodWithGenericParameter")]
        [InlineData("MethodWithNoParameters")]
        public void IndexMethod_IgnoresMethod_IfNonSdkMethod(string method)
        {
            // Arrange
            Mock<IFunctionIndexCollector> indexMock = new Mock<IFunctionIndexCollector>();
            FunctionIndexer product = CreateProductUnderTest();

            // Act
            product.IndexMethodAsync(typeof(FunctionIndexerTests).GetMethod(method), indexMock.Object, CancellationToken.None).GetAwaiter().GetResult();

            // Verify
            indexMock.Verify(i => i.Add(It.IsAny<IFunctionDefinition>(), It.IsAny<FunctionDescriptor>(), It.IsAny<MethodInfo>()), Times.Never);
        }

        [Fact]
        public void IndexMethod_IfMethodReturnsNonTask_Throws()
        {
            // Arrange
            IFunctionIndexCollector index = CreateDummyFunctionIndex();
            FunctionIndexer product = CreateProductUnderTest();

            // Act & Assert
            FunctionIndexingException exception = Assert.Throws<FunctionIndexingException>(
                () => product.IndexMethodAsync(typeof(FunctionIndexerTests).GetMethod("ReturnNonTask"), index,
                    CancellationToken.None).GetAwaiter().GetResult());
            InvalidOperationException innerException = exception.InnerException as InvalidOperationException;
            Assert.NotNull(innerException);
            Assert.Equal("Functions must return Task or void.", innerException.Message);
        }

        [Fact]
        public void IndexMethod_IfMethodReturnsTaskOfTResult_Throws()
        {
            // Arrange
            IFunctionIndexCollector index = CreateDummyFunctionIndex();
            FunctionIndexer product = CreateProductUnderTest();

            // Act & Assert
            FunctionIndexingException exception = Assert.Throws<FunctionIndexingException>(
                () => product.IndexMethodAsync(typeof(FunctionIndexerTests).GetMethod("ReturnGenericTask"), index,
                    CancellationToken.None).GetAwaiter().GetResult());
            InvalidOperationException innerException = exception.InnerException as InvalidOperationException;
            Assert.NotNull(innerException);
            Assert.Equal("Functions must return Task or void.", innerException.Message);
        }

        [Fact]
        public void IndexMethod_IfMethodReturnsVoid_DoesNotThrow()
        {
            // Arrange
            IFunctionIndexCollector index = CreateStubFunctionIndex();
            FunctionIndexer product = CreateProductUnderTest();

            // Act & Assert
            Assert.DoesNotThrow(() => product.IndexMethodAsync(typeof(FunctionIndexerTests).GetMethod("ReturnVoid"),
                index, CancellationToken.None).GetAwaiter().GetResult());
        }

        [Fact]
        public void IndexMethod_IfMethodReturnsTask_DoesNotThrow()
        {
            // Arrange
            IFunctionIndexCollector index = CreateStubFunctionIndex();
            FunctionIndexer product = CreateProductUnderTest();

            // Act & Assert
            Assert.DoesNotThrow(() => product.IndexMethodAsync(typeof(FunctionIndexerTests).GetMethod("ReturnTask"),
                index, CancellationToken.None).GetAwaiter().GetResult());
        }

        [Fact]
        public void IsSdkMethod_ReturnsFalse_IfMethodHasUnresolvedGenericParameter()
        {
            // Arrange
            Mock<IFunctionIndex> indexMock = new Mock<IFunctionIndex>();
            FunctionIndexer product = CreateProductUnderTest();

            // Act
            bool actual = product.IsSdkMethod(typeof(FunctionIndexerTests).GetMethod("MethodWithGenericParameter"));

            // Verify
            Assert.Equal(false, actual);
        }

        [Fact]
        public void IsSdkMethod_ReturnsFalse_IfMethodHasNoParameters()
        {
            // Arrange
            Mock<IFunctionIndex> indexMock = new Mock<IFunctionIndex>();
            FunctionIndexer product = CreateProductUnderTest();

            // Act
            bool actual = product.IsSdkMethod(typeof(FunctionIndexerTests).GetMethod("MethodWithNoParameters"));

            // Verify
            Assert.Equal(false, actual);
        }

        [Fact]
        public void IsSdkMethod_ReturnsTrue_IfMethodHasSdkAttribute()
        {
            // Arrange
            Mock<IFunctionIndex> indexMock = new Mock<IFunctionIndex>();
            FunctionIndexer product = CreateProductUnderTest();

            // Act
            bool actual = product.IsSdkMethod(typeof(FunctionIndexerTests).GetMethod("MethodWithSdkAttribute"));

            // Verify
            Assert.Equal(true, actual);
        }

        [Fact]
        public void IsSdkMethod_ReturnsTrue_IfMethodHasSdkAttributeButNoParameters()
        {
            // Arrange
            Mock<IFunctionIndex> indexMock = new Mock<IFunctionIndex>();
            FunctionIndexer product = CreateProductUnderTest();

            // Act
            bool actual = product.IsSdkMethod(typeof(FunctionIndexerTests).GetMethod("MethodWithSdkAttributeButNoParameters"));

            // Verify
            Assert.Equal(true, actual);
        }

        [Fact]
        public void IsSdkMethod_ReturnsTrue_IfMethodHasSdkParameterAttributes()
        {
            // Arrange
            Mock<IFunctionIndex> indexMock = new Mock<IFunctionIndex>();
            FunctionIndexer product = CreateProductUnderTest();

            // Act
            bool actual = product.IsSdkMethod(typeof(FunctionIndexerTests).GetMethod("MethodWithSdkParameterAttributes"));

            // Verify
            Assert.Equal(true, actual);
        }

        [Fact]
        public void IsSdkMethod_ReturnsFalse_IfMethodHasNoSdkAttributes()
        {
            // Arrange
            Mock<IFunctionIndex> indexMock = new Mock<IFunctionIndex>();
            FunctionIndexer product = CreateProductUnderTest();

            // Act
            bool actual = product.IsSdkMethod(typeof(FunctionIndexerTests).GetMethod("MethodWithUnboundOutParameterAndNoSdkAttribute"));

            // Verify
            Assert.Equal(false, actual);
        }

        private static IFunctionIndexCollector CreateDummyFunctionIndex()
        {
            return new Mock<IFunctionIndexCollector>(MockBehavior.Strict).Object;
        }

        private static FunctionIndexer CreateProductUnderTest()
        {
            return FunctionIndexerFactory.Create(account: null);
        }

        private static IFunctionIndexCollector CreateStubFunctionIndex()
        {
            return new Mock<IFunctionIndexCollector>().Object;
        }

        [NoAutomaticTrigger]
        public static void FailIndexing(string input, out Foo parsed)
        {
            throw new NotImplementedException();
        }

        public static void MethodWithUnboundOutParameterAndNoSdkAttribute(string input, out Foo parsed)
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

        public static void MethodWithNoParameters()
        {
            throw new NotImplementedException();
        }

        [NoAutomaticTrigger]
        public static void MethodWithSdkAttribute(string input, out string output)
        {
            throw new NotImplementedException();
        }

        [NoAutomaticTrigger]
        public static void MethodWithSdkAttributeButNoParameters()
        {
            throw new NotImplementedException();
        }

        public static void MethodWithSdkParameterAttributes([QueueTrigger("queue")] string input, [Blob("container/output")] TextWriter writer)
        {
            throw new NotImplementedException();
        }

        [NoAutomaticTrigger]
        public static int ReturnNonTask()
        {
            throw new NotImplementedException();
        }

        [NoAutomaticTrigger]
        public static Task<int> ReturnGenericTask()
        {
            throw new NotImplementedException();
        }

        [NoAutomaticTrigger]
        public static void ReturnVoid()
        {
            throw new NotImplementedException();
        }

        [NoAutomaticTrigger]
        public static Task ReturnTask()
        {
            throw new NotImplementedException();
        }
    }
}
