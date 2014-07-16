// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
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
            product.IndexMethod(typeof(FunctionIndexerTests).GetMethod("TryParse"), index);

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
                () => product.IndexMethod(typeof(FunctionIndexerTests).GetMethod("FailIndexing"), index));
            InvalidOperationException innerException = exception.InnerException as InvalidOperationException;
            Assert.NotNull(innerException);
            Assert.Equal("Cannot bind parameter 'parsed' to type Foo&.", innerException.Message);
        }

        private static FunctionIndexer CreateProductUnderTest()
        {
            FunctionIndexContext indexContext = new FunctionIndexContext(null, null, null, null);
            FunctionIndexerContext indexerContext = FunctionIndexerContext.CreateDefault(indexContext, null);
            return new FunctionIndexer(indexerContext);
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
    }
}
