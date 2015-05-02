// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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
        [InlineData("MethodWithUnboundOutParameterAndNoJobAttribute")]
        [InlineData("MethodWithGenericParameter")]
        [InlineData("MethodWithNoParameters")]
        public void IndexMethod_IgnoresMethod_IfNonJobMethod(string method)
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
        public void IsJobMethod_ReturnsFalse_IfMethodHasUnresolvedGenericParameter()
        {
            // Arrange
            Mock<IFunctionIndex> indexMock = new Mock<IFunctionIndex>();
            FunctionIndexer product = CreateProductUnderTest();

            // Act
            bool actual = product.IsJobMethod(typeof(FunctionIndexerTests).GetMethod("MethodWithGenericParameter"));

            // Verify
            Assert.Equal(false, actual);
        }

        [Fact]
        public void IsJobMethod_ReturnsFalse_IfMethodHasNoParameters()
        {
            // Arrange
            Mock<IFunctionIndex> indexMock = new Mock<IFunctionIndex>();
            FunctionIndexer product = CreateProductUnderTest();

            // Act
            bool actual = product.IsJobMethod(typeof(FunctionIndexerTests).GetMethod("MethodWithNoParameters"));

            // Verify
            Assert.Equal(false, actual);
        }

        [Fact]
        public void IsJobMethod_ReturnsTrue_IfMethodHasJobAttribute()
        {
            // Arrange
            Mock<IFunctionIndex> indexMock = new Mock<IFunctionIndex>();
            FunctionIndexer product = CreateProductUnderTest();

            // Act
            bool actual = product.IsJobMethod(typeof(FunctionIndexerTests).GetMethod("MethodWithJobAttribute"));

            // Verify
            Assert.Equal(true, actual);
        }

        [Fact]
        public void IsJobMethod_ReturnsTrue_IfMethodHasJobAttributeButNoParameters()
        {
            // Arrange
            Mock<IFunctionIndex> indexMock = new Mock<IFunctionIndex>();
            FunctionIndexer product = CreateProductUnderTest();

            // Act
            bool actual = product.IsJobMethod(typeof(FunctionIndexerTests).GetMethod("MethodWithJobAttributeButNoParameters"));

            // Verify
            Assert.Equal(true, actual);
        }

        [Fact]
        public void IsJobMethod_ReturnsTrue_IfMethodHasJobParameterAttributes()
        {
            // Arrange
            Mock<IFunctionIndex> indexMock = new Mock<IFunctionIndex>();
            FunctionIndexer product = CreateProductUnderTest();

            // Act
            bool actual = product.IsJobMethod(typeof(FunctionIndexerTests).GetMethod("MethodWithJobParameterAttributes"));

            // Verify
            Assert.Equal(true, actual);
        }

        [Fact]
        public void IsJobMethod_ReturnsTrue_IfMethodHasJobParameterAttributes_FromExtensionAssemblies()
        {
            // Arrange
            Mock<IFunctionIndex> indexMock = new Mock<IFunctionIndex>();
            IExtensionRegistry extensions = new DefaultExtensionRegistry();
            extensions.RegisterExtension<ITriggerBindingProvider>(new TestExtensionTriggerBindingProvider());
            extensions.RegisterExtension<IBindingProvider>(new TestExtensionBindingProvider());
            FunctionIndexer product = FunctionIndexerFactory.Create(extensionRegistry: extensions);

            // Act
            bool actual = product.IsJobMethod(typeof(FunctionIndexerTests).GetMethod("MethodWithExtensionJobParameterAttributes"));

            // Verify
            Assert.Equal(true, actual);
        }

        [Fact]
        public void IsJobMethod_ReturnsFalse_IfMethodHasNoSdkAttributes()
        {
            // Arrange
            Mock<IFunctionIndex> indexMock = new Mock<IFunctionIndex>();
            FunctionIndexer product = CreateProductUnderTest();

            // Act
            bool actual = product.IsJobMethod(typeof(FunctionIndexerTests).GetMethod("MethodWithUnboundOutParameterAndNoJobAttribute"));

            // Verify
            Assert.Equal(false, actual);
        }

        private class TestExtensionBindingProvider : IBindingProvider
        {
            public Task<IBinding> TryCreateAsync(BindingProviderContext context)
            {
                throw new NotImplementedException();
            }
        }

        private class TestExtensionTriggerBindingProvider : ITriggerBindingProvider
        {
            public Task<ITriggerBinding> TryCreateAsync(TriggerBindingProviderContext context)
            {
                throw new NotImplementedException();
            }
        }

        [AttributeUsage(AttributeTargets.Parameter)]
        public class ExtensionTrigger : Attribute
        {
            private string _path;

            public ExtensionTrigger(string path)
            {
                _path = path;
            }

            public string Path
            {
                get { return _path; }
            }
        }

        [AttributeUsage(AttributeTargets.Parameter)]
        public class Extension : Attribute
        {
            private string _path;

            public Extension(string path)
            {
                _path = path;
            }

            public string Path
            {
                get { return _path; }
            }
        }

        private static IFunctionIndexCollector CreateDummyFunctionIndex()
        {
            return new Mock<IFunctionIndexCollector>(MockBehavior.Strict).Object;
        }

        private static FunctionIndexer CreateProductUnderTest()
        {
            return FunctionIndexerFactory.Create();
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

        public static void MethodWithUnboundOutParameterAndNoJobAttribute(string input, out Foo parsed)
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
        public static void MethodWithJobAttribute(string input, out string output)
        {
            throw new NotImplementedException();
        }

        [NoAutomaticTrigger]
        public static void MethodWithJobAttributeButNoParameters()
        {
            throw new NotImplementedException();
        }

        public static void MethodWithJobParameterAttributes([QueueTrigger("queue")] string input, [Blob("container/output")] TextWriter writer)
        {
            throw new NotImplementedException();
        }

        public static void MethodWithExtensionJobParameterAttributes([ExtensionTrigger("path")] string input, [Extension("path")] TextWriter writer)
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
