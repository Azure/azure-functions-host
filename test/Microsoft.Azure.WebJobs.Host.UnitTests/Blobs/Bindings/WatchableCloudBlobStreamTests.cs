// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Blobs.Bindings;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Moq;
using Xunit;
using Xunit.Extensions;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Blobs.Bindings
{
    public class WatchableCloudBlobStreamTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CanRead_DelegatesToInnerStreamCanRead(bool expected)
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.CanRead).Returns(expected);
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            bool canRead = product.CanRead;

            // Assert
            Assert.Equal(expected, canRead);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CanSeek_DelegatesToInnerStreamCanSeek(bool expected)
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.CanSeek).Returns(expected);
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            bool canSeek = product.CanSeek;

            // Assert
            Assert.Equal(expected, canSeek);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CanTimeout_DelegatesToInnerStreamCanTimeout(bool expected)
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.CanTimeout).Returns(expected);
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            bool canTimeout = product.CanTimeout;

            // Assert
            Assert.Equal(expected, canTimeout);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CanWrite_DelegatesToInnerStreamCanWrite(bool expected)
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.CanWrite).Returns(expected);
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            bool canWrite = product.CanWrite;

            // Assert
            Assert.Equal(expected, canWrite);
        }

        [Fact]
        public void Length_DelegatesToInnerStreamLength()
        {
            // Arrange
            long expected = 123;
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.Length).Returns(expected);
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            long length = product.Length;

            // Assert
            Assert.Equal(expected, length);
        }

        [Fact]
        public void GetPosition_DelegatesToInnerStreamGetPosition()
        {
            // Arrange
            long expected = 123;
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.Position).Returns(expected);
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            long position = product.Position;

            // Assert
            Assert.Equal(expected, position);
        }

        [Fact]
        public void SetPosition_DelegatesToInnerStreamSetPosition()
        {
            // Arrange
            long expected = 123;
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.SetupSet(s => s.Position = expected).Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            product.Position = expected;

            // Assert
            innerStreamMock.Verify();
        }

        [Fact]
        public void GetReadTimeout_DelegatesToInnerStreamGetReadTimeout()
        {
            // Arrange
            int expected = 123;
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.ReadTimeout).Returns(expected);
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            int readTimeout = product.ReadTimeout;

            // Assert
            Assert.Equal(expected, readTimeout);
        }

        [Fact]
        public void SetReadTimeout_DelegatesToInnerStreamSetReadTimeout()
        {
            // Arrange
            int expected = 123;
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.SetupSet(s => s.ReadTimeout = expected).Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            product.ReadTimeout = expected;

            // Assert
            innerStreamMock.Verify();
        }

        [Fact]
        public void GetWriteTimeout_DelegatesToInnerStreamGetWriteTimeout()
        {
            // Arrange
            int expected = 123;
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.WriteTimeout).Returns(expected);
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            int writeTimeout = product.WriteTimeout;

            // Assert
            Assert.Equal(expected, writeTimeout);
        }

        [Fact]
        public void SetWriteTimeout_DelegatesToInnerStreamSetWriteTimeout()
        {
            // Arrange
            int expected = 123;
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.SetupSet(s => s.WriteTimeout = expected).Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            product.WriteTimeout = expected;

            // Assert
            innerStreamMock.Verify();
        }

        [Fact]
        public void BeginCommit_DelegatesToInnerStreamBeginCommit()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginCommit()
                .ReturnsUncompleted()
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            CloudBlobStream product = CreateProductUnderTest(innerStream);

            AsyncCallback callback = null;
            object state = null;

            // Act
            product.BeginCommit(callback, state);

            // Assert
            innerStreamMock.Verify();
        }

        [Fact]
        public void BeginCommit_WhenInnerStreamThrows_PropogatesException()
        {
            // Arrange
            Exception expectedException = new Exception();
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginCommit()
                .Throws(expectedException);
            CloudBlobStream innerStream = innerStreamMock.Object;
            CloudBlobStream product = CreateProductUnderTest(innerStream);

            AsyncCallback callback = null;
            object state = null;

            // Act & Assert
            Exception exception = Assert.Throws<Exception>(() => product.BeginCommit(callback, state));
            Assert.Same(expectedException, exception);
        }

        [Fact]
        public void BeginCommit_WhenNotYetCompleted_ReturnsUncompletedResult()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginCommit()
                .ReturnsUncompleted();
            CloudBlobStream innerStream = innerStreamMock.Object;
            CloudBlobStream product = CreateProductUnderTest(innerStream);

            AsyncCallback callback = null;
            object expectedState = new object();

            // Act
            IAsyncResult result = product.BeginCommit(callback, expectedState);

            // Assert
            ExpectedAsyncResult expectedResult = new ExpectedAsyncResult
            {
                AsyncState = expectedState,
                CompletedSynchronously = false,
                IsCompleted = false
            };
            AssertEqual(expectedResult, result, disposeActual: false);

            // Cleanup
            result.AsyncWaitHandle.Dispose();
        }

        [Fact]
        public void BeginCommit_Cancel_WhenNotYetCompleted_CallsInnerCancellableAsyncResultCancel()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            CancellableAsyncCompletionSource spy = new CancellableAsyncCompletionSource();
            innerStreamMock
                .SetupBeginCommit()
                .ReturnsCompletingAsynchronously(spy);
            CloudBlobStream innerStream = innerStreamMock.Object;
            CloudBlobStream product = CreateProductUnderTest(innerStream);

            AsyncCallback callback = null;
            object expectedState = new object();

            ICancellableAsyncResult result = product.BeginCommit(callback, expectedState);
            Assert.NotNull(result); // Guard

            // Act
            result.Cancel();

            // Assert
            Assert.True(spy.Canceled);

            // Cleanup
            result.AsyncWaitHandle.Dispose();
        }

        [Fact]
        public void BeginCommit_WhenCompletedSynchronously_CallsCallbackAndReturnsCompletedResult()
        {
            // Arrange
            object expectedState = new object();
            ExpectedAsyncResult expectedResult = new ExpectedAsyncResult
            {
                AsyncState = expectedState,
                CompletedSynchronously = true,
                IsCompleted = true
            };

            bool callbackCalled = false;
            IAsyncResult callbackResult = null;

            AsyncCallback callback = (ar) =>
            {
                callbackResult = ar;
                AssertEqual(expectedResult, ar);
                callbackCalled = true;
            };

            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginCommit()
                .ReturnsCompletedSynchronously();
            innerStreamMock.SetupEndCommit();
            CloudBlobStream innerStream = innerStreamMock.Object;
            CloudBlobStream product = CreateProductUnderTest(innerStream);

            // Act
            IAsyncResult result = product.BeginCommit(callback, expectedState);

            // Assert
            Assert.True(callbackCalled);
            // An AsyncCallback must be called with the same IAsyncResult instance as the Begin method returned.
            Assert.Same(result, callbackResult);
            AssertEqual(expectedResult, result, disposeActual: true);
        }

        [Fact]
        public void BeginCommit_WhenCompletedAsynchronously_CallsCallbackAndCompletesResult()
        {
            // Arrange
            object expectedState = new object();
            ExpectedAsyncResult expectedResult = new ExpectedAsyncResult
            {
                AsyncState = expectedState,
                CompletedSynchronously = false,
                IsCompleted = true
            };

            bool callbackCalled = false;
            IAsyncResult callbackResult = null;
            CloudBlobStream product = null;

            AsyncCallback callback = (ar) =>
            {
                callbackResult = ar;
                AssertEqual(expectedResult, ar);
                callbackCalled = true;
            };

            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            CancellableAsyncCompletionSource completion = new CancellableAsyncCompletionSource();
            innerStreamMock
                .SetupBeginCommit()
                .ReturnsCompletingAsynchronously(completion);
            innerStreamMock.SetupEndCommit();
            CloudBlobStream innerStream = innerStreamMock.Object;
            product = CreateProductUnderTest(innerStream);

            IAsyncResult result = product.BeginCommit(callback, expectedState);

            // Act
            completion.Complete();

            // Assert
            Assert.True(callbackCalled);
            // An AsyncCallback must be called with the same IAsyncResult instance as the Begin method returned.
            Assert.Same(result, callbackResult);
            AssertEqual(expectedResult, result, disposeActual: true);
        }

        [Fact]
        public void EndCommit_DelegatesToInnerStreamEndCommit()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            CancellableAsyncCompletionSource completion = new CancellableAsyncCompletionSource();
            innerStreamMock
                .SetupBeginCommit()
                .ReturnsCompletingAsynchronously(completion);
            innerStreamMock
                .Setup(s => s.EndCommit(It.Is<IAsyncResult>((ar) => ar == completion.AsyncResult)))
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            CloudBlobStream product = CreateProductUnderTest(innerStream);

            AsyncCallback callback = null;
            object state = null;

            IAsyncResult result = product.BeginCommit(callback, state);
            completion.Complete();

            // Act
            product.EndCommit(result);

            // Assert
            innerStreamMock.Verify();
        }

        [Fact]
        public void EndCommit_DuringCallback_DelegatesToInnerStreamEndCommit()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            CancellableAsyncCompletionSource completion = new CancellableAsyncCompletionSource();
            innerStreamMock
                .SetupBeginCommit()
                .ReturnsCompletingAsynchronously(completion);
            innerStreamMock
                .Setup(s => s.EndCommit(It.Is<IAsyncResult>(ar => ar == completion.AsyncResult)))
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            CloudBlobStream product = CreateProductUnderTest(innerStream);

            bool callbackCalled = false;
            AsyncCallback callback = (ar) =>
            {
                product.EndCommit(ar);
                callbackCalled = true;
            };
            object state = null;

            IAsyncResult result = product.BeginCommit(callback, state);

            // Act
            completion.Complete();

            // Assert
            Assert.True(callbackCalled);
            innerStreamMock.Verify();
        }

        [Fact]
        public void EndCommit_WhenInnerStreamThrows_PropogatesException()
        {
            // Arrange
            Exception expectedException = new Exception();
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            CancellableAsyncCompletionSource completion = new CancellableAsyncCompletionSource();
            innerStreamMock
                .SetupBeginCommit()
                .ReturnsCompletingAsynchronously(completion);
            innerStreamMock
                .SetupEndCommit()
                .Throws(expectedException);
            CloudBlobStream innerStream = innerStreamMock.Object;
            CloudBlobStream product = CreateProductUnderTest(innerStream);

            AsyncCallback callback = null;
            object state = null;
            IAsyncResult result = product.BeginCommit(callback, state);
            completion.Complete();

            // Act & Assert
            Exception exception = Assert.Throws<Exception>(() => product.EndCommit(result));
            Assert.Same(expectedException, exception);
        }

        [Fact]
        public void EndCommit_DuringCallback_WhenInnerStreamThrows_PropogatesException()
        {
            // Arrange
            Exception expectedException = new Exception();
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            CancellableAsyncCompletionSource completion = new CancellableAsyncCompletionSource();
            innerStreamMock
                .SetupBeginCommit()
                .ReturnsCompletingAsynchronously(completion);
            innerStreamMock
                .SetupEndCommit()
                .Throws(expectedException);
            CloudBlobStream innerStream = innerStreamMock.Object;
            CloudBlobStream product = CreateProductUnderTest(innerStream);

            bool callbackCalled = false;
            IAsyncResult result = null;
            AsyncCallback callback = (_) =>
            {
                Exception exception = Assert.Throws<Exception>(() => product.EndCommit(result));
                Assert.Same(expectedException, exception);
                callbackCalled = true;
            };
            object state = null;
            result = product.BeginCommit(callback, state);

            // Act & Assert
            completion.Complete();
            Assert.True(callbackCalled);
        }

        [Fact]
        public void BeginFlush_DelegatesToInnerStreamBeginFlush()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginFlush()
                .ReturnsUncompleted()
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            CloudBlobStream product = CreateProductUnderTest(innerStream);

            AsyncCallback callback = null;
            object state = null;

            // Act
            product.BeginFlush(callback, state);

            // Assert
            innerStreamMock.Verify();
        }

        [Fact]
        public void BeginFlush_WhenInnerStreamThrows_PropogatesException()
        {
            // Arrange
            Exception expectedException = new Exception();
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginFlush()
                .Throws(expectedException);
            CloudBlobStream innerStream = innerStreamMock.Object;
            CloudBlobStream product = CreateProductUnderTest(innerStream);

            AsyncCallback callback = null;
            object state = null;

            // Act & Assert
            Exception exception = Assert.Throws<Exception>(() => product.BeginFlush(callback, state));
            Assert.Same(expectedException, exception);
        }

        [Fact]
        public void BeginFlush_WhenNotYetCompleted_ReturnsUncompletedResult()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginFlush()
                .ReturnsUncompleted();
            CloudBlobStream innerStream = innerStreamMock.Object;
            CloudBlobStream product = CreateProductUnderTest(innerStream);

            AsyncCallback callback = null;
            object expectedState = new object();

            // Act
            IAsyncResult result = product.BeginFlush(callback, expectedState);

            // Assert
            ExpectedAsyncResult expectedResult = new ExpectedAsyncResult
            {
                AsyncState = expectedState,
                CompletedSynchronously = false,
                IsCompleted = false
            };
            AssertEqual(expectedResult, result, disposeActual: false);

            // Cleanup
            result.AsyncWaitHandle.Dispose();
        }

        [Fact]
        public void BeginFlush_Cancel_WhenNotYetCompleted_CallsInnerCancellableAsyncResultCancel()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            CancellableAsyncCompletionSource spy = new CancellableAsyncCompletionSource();
            innerStreamMock
                .SetupBeginFlush()
                .ReturnsCompletingAsynchronously(spy);
            CloudBlobStream innerStream = innerStreamMock.Object;
            CloudBlobStream product = CreateProductUnderTest(innerStream);

            AsyncCallback callback = null;
            object state = null;

            ICancellableAsyncResult result = product.BeginFlush(callback, state);
            Assert.NotNull(result); // Guard

            // Act
            result.Cancel();

            // Assert
            Assert.True(spy.Canceled);

            // Cleanup
            result.AsyncWaitHandle.Dispose();
        }

        [Fact]
        public void BeginFlush_WhenCompletedSynchronously_CallsCallbackAndReturnsCompletedResult()
        {
            // Arrange
            object expectedState = new object();
            ExpectedAsyncResult expectedResult = new ExpectedAsyncResult
            {
                AsyncState = expectedState,
                CompletedSynchronously = true,
                IsCompleted = true
            };

            bool callbackCalled = false;
            IAsyncResult callbackResult = null;
            CloudBlobStream product = null;

            AsyncCallback callback = (ar) =>
            {
                callbackResult = ar;
                AssertEqual(expectedResult, ar);
                callbackCalled = true;
            };

            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginFlush()
                .ReturnsCompletedSynchronously();
            innerStreamMock.SetupEndFlush();
            CloudBlobStream innerStream = innerStreamMock.Object;
            product = CreateProductUnderTest(innerStream);

            // Act
            IAsyncResult result = product.BeginFlush(callback, expectedState);

            // Assert
            Assert.True(callbackCalled);
            // An AsyncCallback must be called with the same IAsyncResult instance as the Begin method returned.
            Assert.Same(result, callbackResult);
            AssertEqual(expectedResult, result, disposeActual: true);
        }

        [Fact]
        public void BeginFlush_WhenCompletedAsynchronously_CallsCallbackAndCompletesResult()
        {
            // Arrange
            object expectedState = new object();
            ExpectedAsyncResult expectedResult = new ExpectedAsyncResult
            {
                AsyncState = expectedState,
                CompletedSynchronously = false,
                IsCompleted = true
            };

            bool callbackCalled = false;
            IAsyncResult callbackResult = null;
            CloudBlobStream product = null;

            AsyncCallback callback = (ar) =>
            {
                callbackResult = ar;
                AssertEqual(expectedResult, ar);
                callbackCalled = true;
            };

            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            CancellableAsyncCompletionSource completion = new CancellableAsyncCompletionSource();
            innerStreamMock
                .SetupBeginFlush()
                .ReturnsCompletingAsynchronously(completion);
            innerStreamMock
                .SetupEndRead()
                .Returns(-1);
            CloudBlobStream innerStream = innerStreamMock.Object;
            product = CreateProductUnderTest(innerStream);

            IAsyncResult result = product.BeginFlush(callback, expectedState);

            // Act
            completion.Complete();

            // Assert
            Assert.True(callbackCalled);
            // An AsyncCallback must be called with the same IAsyncResult instance as the Begin method returned.
            Assert.Same(result, callbackResult);
            AssertEqual(expectedResult, result, disposeActual: true);
        }

        [Fact]
        public void EndFlush_DelegatesToInnerStreamEndFlush()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            CancellableAsyncCompletionSource completion = new CancellableAsyncCompletionSource();
            innerStreamMock
                .SetupBeginFlush()
                .ReturnsCompletingAsynchronously(completion);
            innerStreamMock
                .Setup(s => s.EndFlush(It.Is<IAsyncResult>((ar) => ar == completion.AsyncResult)))
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            CloudBlobStream product = CreateProductUnderTest(innerStream);

            AsyncCallback callback = null;
            object state = null;

            IAsyncResult result = product.BeginFlush(callback, state);
            completion.Complete();

            // Act
            product.EndFlush(result);

            // Assert
            innerStreamMock.Verify();
        }

        [Fact]
        public void EndFlush_DuringCallback_DelegatesToInnerStreamEndFlush()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            CancellableAsyncCompletionSource completion = new CancellableAsyncCompletionSource();
            innerStreamMock
                .SetupBeginFlush()
                .ReturnsCompletingAsynchronously(completion);
            innerStreamMock
                .Setup(s => s.EndFlush(It.Is<IAsyncResult>((ar) => ar == completion.AsyncResult)))
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            CloudBlobStream product = CreateProductUnderTest(innerStream);

            bool callbackCalled = false;
            AsyncCallback callback = (ar) =>
            {
                product.EndFlush(ar);
                callbackCalled = true;
            };
            object state = null;

            IAsyncResult result = product.BeginFlush(callback, state);

            // Act
            completion.Complete();

            // Assert
            Assert.True(callbackCalled);
            innerStreamMock.Verify();
        }

        [Fact]
        public void EndFlush_WhenInnerStreamThrows_PropogatesException()
        {
            // Arrange
            Exception expectedException = new Exception();
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            CancellableAsyncCompletionSource completion = new CancellableAsyncCompletionSource();
            innerStreamMock
                .SetupBeginFlush()
                .ReturnsCompletingAsynchronously(completion);
            innerStreamMock
                .SetupEndFlush()
                .Throws(expectedException);
            CloudBlobStream innerStream = innerStreamMock.Object;
            CloudBlobStream product = CreateProductUnderTest(innerStream);

            AsyncCallback callback = null;
            object state = null;
            IAsyncResult result = product.BeginFlush(callback, state);
            completion.Complete();

            // Act & Assert
            Exception exception = Assert.Throws<Exception>(() => product.EndFlush(result));
            Assert.Same(expectedException, exception);
        }

        [Fact]
        public void EndFlush_DuringCallback_WhenInnerStreamThrows_PropogatesException()
        {
            // Arrange
            Exception expectedException = new Exception();
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            CancellableAsyncCompletionSource completion = new CancellableAsyncCompletionSource();
            innerStreamMock
                .SetupBeginFlush()
                .ReturnsCompletingAsynchronously(completion);
            innerStreamMock
                .SetupEndFlush()
                .Throws(expectedException);
            CloudBlobStream innerStream = innerStreamMock.Object;
            CloudBlobStream product = CreateProductUnderTest(innerStream);

            bool callbackCalled = false;
            IAsyncResult result = null;
            AsyncCallback callback = (_) =>
            {
                Exception exception = Assert.Throws<Exception>(() => product.EndFlush(result));
                Assert.Same(expectedException, exception);
                callbackCalled = true;
            };
            object state = null;
            result = product.BeginFlush(callback, state);

            // Act & Assert
            completion.Complete();
            Assert.True(callbackCalled);
        }

        [Fact]
        public void BeginRead_DelegatesToInnerStreamBeginRead()
        {
            // Arrange
            byte[] expectedBuffer = new byte[0];
            int expectedOffset = 123;
            int expectedCount = 456;

            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.BeginRead(expectedBuffer, expectedOffset, expectedCount, It.IsAny<AsyncCallback>(),
                    It.IsAny<object>()))
                .ReturnsUncompleted()
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            AsyncCallback callback = null;
            object state = null;

            // Act
            product.BeginRead(expectedBuffer, expectedOffset, expectedCount, callback, state);

            // Assert
            innerStreamMock.Verify();
        }

        [Fact]
        public void BeginRead_WhenInnerStreamThrows_PropogatesException()
        {
            // Arrange
            Exception expectedException = new Exception();
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginRead()
                .Throws(expectedException);
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            AsyncCallback callback = null;
            object state = null;

            // Act & Assert
            Exception exception = Assert.Throws<Exception>(() => product.BeginRead(buffer, offset, count, callback,
                state));
            Assert.Same(expectedException, exception);
        }

        [Fact]
        public void BeginRead_WhenNotYetCompleted_ReturnsUncompletedResult()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginRead()
                .ReturnsUncompleted();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            AsyncCallback callback = null;
            object expectedState = new object();

            // Act
            IAsyncResult result = product.BeginRead(buffer, offset, count, callback, expectedState);

            // Assert
            ExpectedAsyncResult expectedResult = new ExpectedAsyncResult
            {
                AsyncState = expectedState,
                CompletedSynchronously = false,
                IsCompleted = false
            };
            AssertEqual(expectedResult, result, disposeActual: false);

            // Cleanup
            result.AsyncWaitHandle.Dispose();
        }

        [Fact]
        public void BeginRead_WhenCompletedSynchronously_CallsCallbackAndReturnsCompletedResult()
        {
            // Arrange
            object expectedState = new object();
            ExpectedAsyncResult expectedResult = new ExpectedAsyncResult
            {
                AsyncState = expectedState,
                CompletedSynchronously = true,
                IsCompleted = true
            };

            bool callbackCalled = false;
            IAsyncResult callbackResult = null;
            Stream product = null;

            AsyncCallback callback = (ar) =>
            {
                callbackResult = ar;
                AssertEqual(expectedResult, ar);
                callbackCalled = true;
            };

            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginRead()
                .ReturnsCompletedSynchronously();
            innerStreamMock
                .SetupEndRead()
                .Returns(-1);
            CloudBlobStream innerStream = innerStreamMock.Object;
            product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;

            // Act
            IAsyncResult result = product.BeginRead(buffer, offset, count, callback, expectedState);

            // Assert
            Assert.True(callbackCalled);
            // An AsyncCallback must be called with the same IAsyncResult instance as the Begin method returned.
            Assert.Same(result, callbackResult);
            AssertEqual(expectedResult, result, disposeActual: true);
        }

        [Fact]
        public void BeginRead_WhenCompletedAsynchronously_CallsCallbackAndCompletesResult()
        {
            // Arrange
            object expectedState = new object();
            ExpectedAsyncResult expectedResult = new ExpectedAsyncResult
            {
                AsyncState = expectedState,
                CompletedSynchronously = false,
                IsCompleted = true
            };

            bool callbackCalled = false;
            IAsyncResult callbackResult = null;
            Stream product = null;

            AsyncCallback callback = (ar) =>
            {
                callbackResult = ar;
                AssertEqual(expectedResult, ar);
                callbackCalled = true;
            };

            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            AsyncCompletionSource completion = new AsyncCompletionSource();
            innerStreamMock
                .SetupBeginRead()
                .ReturnsCompletingAsynchronously(completion);
            innerStreamMock
                .SetupEndRead()
                .Returns(-1);
            CloudBlobStream innerStream = innerStreamMock.Object;
            product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;

            IAsyncResult result = product.BeginRead(buffer, offset, count, callback, expectedState);

            // Act
            completion.Complete();

            // Assert
            Assert.True(callbackCalled);
            // An AsyncCallback must be called with the same IAsyncResult instance as the Begin method returned.
            Assert.Same(result, callbackResult);
            AssertEqual(expectedResult, result, disposeActual: true);
        }

        [Fact]
        public void EndRead_DelegatesToInnerStreamEndRead()
        {
            // Arrange
            int expectedBytesRead = 789;

            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            AsyncCompletionSource completion = new AsyncCompletionSource();
            innerStreamMock
                .SetupBeginRead()
                .ReturnsCompletingAsynchronously(completion);
            innerStreamMock
                .Setup(s => s.EndRead(It.Is<IAsyncResult>(ar => ar == completion.AsyncResult)))
                .Returns(expectedBytesRead)
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            AsyncCallback callback = null;
            object state = null;

            IAsyncResult result = product.BeginRead(buffer, offset, count, callback, state);
            completion.Complete();

            // Act
            int bytesRead = product.EndRead(result);

            // Assert
            Assert.Equal(expectedBytesRead, bytesRead);
            innerStreamMock.Verify();
        }

        [Fact]
        public void EndRead_DuringCallback_DelegatesToInnerStreamEndRead()
        {
            // Arrange
            int expectedBytesRead = 789;

            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            AsyncCompletionSource completion = new AsyncCompletionSource();
            innerStreamMock
                .SetupBeginRead()
                .ReturnsCompletingAsynchronously(completion);
            innerStreamMock
                .Setup(s => s.EndRead(It.Is<IAsyncResult>(ar => ar == completion.AsyncResult)))
                .Returns(expectedBytesRead)
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            int bytesRead = 0;

            bool callbackCalled = false;
            AsyncCallback callback = (ar) =>
            {
                bytesRead = product.EndRead(ar);
                callbackCalled = true;
            };
            object state = null;

            IAsyncResult result = product.BeginRead(buffer, offset, count, callback, state);

            // Act
            completion.Complete();

            // Assert
            Assert.True(callbackCalled);
            Assert.Equal(expectedBytesRead, bytesRead);
            innerStreamMock.Verify();
        }

        [Fact]
        public void EndRead_WhenInnerStreamThrows_PropogatesException()
        {
            // Arrange
            Exception expectedException = new Exception();
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            AsyncCompletionSource completion = new AsyncCompletionSource();
            innerStreamMock
                .SetupBeginRead()
                .ReturnsCompletingAsynchronously(completion);
            innerStreamMock
                .SetupEndRead()
                .Throws(expectedException);
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            AsyncCallback callback = null;
            object state = null;
            IAsyncResult result = product.BeginRead(buffer, offset, count, callback, state);
            completion.Complete();

            // Act & Assert
            Exception exception = Assert.Throws<Exception>(() => product.EndRead(result));
            Assert.Same(expectedException, exception);
        }

        [Fact]
        public void BeginWrite_DelegatesToInnerStreamBeginWrite()
        {
            // Arrange
            byte[] expectedBuffer = new byte[0];
            int expectedOffset = 123;
            int expectedCount = 456;

            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.BeginWrite(expectedBuffer, expectedOffset, expectedCount, It.IsAny<AsyncCallback>(),
                    It.IsAny<object>()))
                .ReturnsUncompleted()
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            AsyncCallback callback = null;
            object state = null;

            // Act
            product.BeginWrite(expectedBuffer, expectedOffset, expectedCount, callback, state);

            // Assert
            innerStreamMock.Verify();
        }

        [Fact]
        public void BeginWrite_WhenInnerStreamThrows_PropogatesException()
        {
            // Arrange
            Exception expectedException = new Exception();
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginWrite()
                .Throws(expectedException);
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            AsyncCallback callback = null;
            object state = null;

            // Act & Assert
            Exception exception = Assert.Throws<Exception>(() => product.BeginWrite(buffer, offset, count, callback,
                state));
            Assert.Same(expectedException, exception);
        }

        [Fact]
        public void BeginWrite_WhenNotYetCompleted_ReturnsUncompletedResult()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginWrite()
                .ReturnsUncompleted();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            AsyncCallback callback = null;
            object expectedState = new object();

            // Act
            IAsyncResult result = product.BeginWrite(buffer, offset, count, callback, expectedState);

            // Assert
            ExpectedAsyncResult expectedResult = new ExpectedAsyncResult
            {
                AsyncState = expectedState,
                CompletedSynchronously = false,
                IsCompleted = false
            };
            AssertEqual(expectedResult, result, disposeActual: false);

            // Cleanup
            result.AsyncWaitHandle.Dispose();
        }

        [Fact]
        public void BeginWrite_WhenCompletedSynchronously_CallsCallbackAndReturnsCompletedResult()
        {
            // Arrange
            object expectedState = new object();
            ExpectedAsyncResult expectedResult = new ExpectedAsyncResult
            {
                AsyncState = expectedState,
                CompletedSynchronously = true,
                IsCompleted = true
            };

            bool callbackCalled = false;
            IAsyncResult callbackResult = null;
            Stream product = null;

            AsyncCallback callback = (ar) =>
            {
                callbackResult = ar;
                AssertEqual(expectedResult, ar);
                callbackCalled = true;
            };

            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.BeginWrite(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<AsyncCallback>(), It.IsAny<object>()))
                .Returns<byte[], int, int, AsyncCallback, object>((i1, i2, i3, c, s) =>
            {
                IAsyncResult r = new CompletedAsyncResult(s);
                if (c != null) c.Invoke(r);
                return r;
            });

            innerStreamMock.SetupEndWrite();
            CloudBlobStream innerStream = innerStreamMock.Object;
            product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;

            // Act
            IAsyncResult result = product.BeginWrite(buffer, offset, count, callback, expectedState);

            // Assert
            Assert.True(callbackCalled);
            // An AsyncCallback must be called with the same IAsyncResult instance as the Begin method returned.
            Assert.Same(result, callbackResult);
            AssertEqual(expectedResult, result, disposeActual: true);
        }

        [Fact]
        public void BeginWrite_WhenCompletedAsynchronously_CallsCallbackAndCompletesResult()
        {
            // Arrange
            object expectedState = new object();
            ExpectedAsyncResult expectedResult = new ExpectedAsyncResult
            {
                AsyncState = expectedState,
                CompletedSynchronously = false,
                IsCompleted = true
            };

            bool callbackCalled = false;
            IAsyncResult callbackResult = null;
            Stream product = null;

            AsyncCallback callback = (ar) =>
            {
                callbackResult = ar;
                AssertEqual(expectedResult, ar);
                callbackCalled = true;
            };

            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            AsyncCompletionSource completion = new AsyncCompletionSource();
            innerStreamMock
                .SetupBeginWrite()
                .ReturnsCompletingAsynchronously(completion);
            innerStreamMock.SetupEndWrite();
            CloudBlobStream innerStream = innerStreamMock.Object;
            product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;

            IAsyncResult result = product.BeginWrite(buffer, offset, count, callback, expectedState);

            // Act
            completion.Complete();

            // Assert
            Assert.True(callbackCalled);
            // An AsyncCallback must be called with the same IAsyncResult instance as the Begin method returned.
            Assert.Same(result, callbackResult);
            AssertEqual(expectedResult, result, disposeActual: true);
        }

        [Fact]
        public void EndWrite_DelegatesToInnerStreamEndWrite()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            AsyncCompletionSource completion = new AsyncCompletionSource();
            innerStreamMock
                .SetupBeginWrite()
                .ReturnsCompletingAsynchronously(completion);
            innerStreamMock
                .Setup(s => s.EndWrite(It.Is<IAsyncResult>((ar) => ar == completion.AsyncResult)))
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            AsyncCallback callback = null;
            object state = null;

            IAsyncResult result = product.BeginWrite(buffer, offset, count, callback, state);
            completion.Complete();

            // Act
            product.EndWrite(result);

            // Assert
            innerStreamMock.Verify();
        }

        [Fact]
        public void EndWrite_DuringCallback_DelegatesToInnerStreamEndWrite()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            AsyncCompletionSource completion = new AsyncCompletionSource();
            innerStreamMock
                .SetupBeginWrite()
                .ReturnsCompletingAsynchronously(completion);
            innerStreamMock
                .Setup(s => s.EndWrite(It.Is<IAsyncResult>((ar) => ar == completion.AsyncResult)))
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;

            bool callbackCalled = false;
            AsyncCallback callback = (ar) =>
            {
                product.EndWrite(ar);
                callbackCalled = true;
            };
            object state = null;

            IAsyncResult result = product.BeginWrite(buffer, offset, count, callback, state);

            // Act
            completion.Complete();

            // Assert
            Assert.True(callbackCalled);
            innerStreamMock.Verify();
        }

        [Fact]
        public void EndWrite_WhenInnerStreamThrows_PropogatesException()
        {
            // Arrange
            Exception expectedException = new Exception();
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            AsyncCompletionSource completion = new AsyncCompletionSource();
            innerStreamMock
                .SetupBeginWrite()
                .ReturnsCompletingAsynchronously(completion);
            innerStreamMock
                .SetupEndWrite()
                .Throws(expectedException);
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            AsyncCallback callback = null;
            object state = null;
            IAsyncResult result = product.BeginWrite(buffer, offset, count, callback, state);
            completion.Complete();

            // Act & Assert
            Exception exception = Assert.Throws<Exception>(() => product.EndWrite(result));
            Assert.Same(expectedException, exception);
        }

        [Fact]
        public void Close_DelegatesToInnerStreamClose()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.Commit());
            innerStreamMock
                .Setup(s => s.Close())
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            product.Close();

            // Assert
            innerStreamMock.Verify();
        }

        [Fact]
        public void Commit_DelegatesToInnerStreamCommit()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.Commit())
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            CloudBlobStream product = CreateProductUnderTest(innerStream);

            // Act
            product.Commit();

            // Assert
            innerStreamMock.Verify();
        }

        [Fact]
        public void CopyToAsync_DelegatesToInnerStreamCopyToAsync()
        {
            // Arrange
            Stream expectedDestination = CreateDummyStream();
            int expectedBufferSize = 123;
            CancellationToken expectedCancellationToken = new CancellationToken(canceled: true);
            Task expectedTask = new Task(() => { });

            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.CopyToAsync(expectedDestination, expectedBufferSize, expectedCancellationToken))
                .Returns(expectedTask)
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            Task task = product.CopyToAsync(expectedDestination, expectedBufferSize, expectedCancellationToken);

            // Assert
            Assert.Same(task, expectedTask);
            innerStreamMock.Verify();
        }

        [Fact]
        public void Flush_DelegatesToInnerStreamFlush()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.Flush())
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            product.Flush();

            // Assert
            innerStreamMock.Verify();
        }

        // The Storage NuGet package for .NET 4.0 can't implement FlushAsync, since that is a .NET 4.5-only method.
        // Rely on BeginFlush/EndFlush instead of FlushAsync.

        [Fact]
        public void FlushAsync_DelegatesToInnerStreamBeginEndFlush()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            CancellableAsyncCompletionSource completion = new CancellableAsyncCompletionSource();
            innerStreamMock
                .SetupBeginFlush()
                .ReturnsCompletingAsynchronously(completion)
                .Verifiable();
            innerStreamMock
                .Setup(s => s.EndFlush(It.Is<IAsyncResult>((ar) => ar == completion.AsyncResult)))
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            CancellationToken cancellationToken = CancellationToken.None;
            Task task = product.FlushAsync(cancellationToken);

            // Act
            completion.Complete();

            // Assert
            innerStreamMock.Verify();

            // Cleanup
            task.GetAwaiter().GetResult();
        }

        [Fact]
        public async Task FlushAsync_WhenInnerStreamBeginFlushThrows_PropogatesException()
        {
            // Arrange
            Exception expectedException = new Exception();
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginFlush()
                .Throws(expectedException);
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            CancellationToken cancellationToken = CancellationToken.None;

            // Act & Assert
            Exception exception = await Assert.ThrowsAsync<Exception>(() => product.FlushAsync(cancellationToken));
            Assert.Same(expectedException, exception);
        }

        [Fact]
        public void FlushAsync_WhenInnerStreamBeginFlushHasNotYetCompleted_ReturnsIncompleteTask()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginFlush()
                .ReturnsUncompleted();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task task = product.FlushAsync(cancellationToken);

            // Assert
            Assert.NotNull(task);
            Assert.False(task.IsCompleted);
        }

        [Fact]
        public void FlushAsync_WhenInnerStreamBeginFlushCompletedSynchronously_ReturnsRanToCompletionTask()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginFlush()
                .ReturnsCompletedSynchronously();
            innerStreamMock.SetupEndFlush();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task task = product.FlushAsync(cancellationToken);

            // Assert
            Assert.NotNull(task);
            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
        }

        [Fact]
        public void FlushAsync_WhenInnerStreamBeginFlushCompletedAsynchronously_ReturnsRanToCompletionTask()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            CancellableAsyncCompletionSource completion = new CancellableAsyncCompletionSource();
            innerStreamMock
                .SetupBeginFlush()
                .ReturnsCompletingAsynchronously(completion);
            innerStreamMock.SetupEndFlush();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            CancellationToken cancellationToken = CancellationToken.None;

            Task task = product.FlushAsync(cancellationToken);

            // Act
            completion.Complete();

            // Assert
            Assert.NotNull(task);
            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
        }

        [Fact]
        public void FlushAsync_WhenInnerStreamEndReadThrowsOperationCanceledException_ReturnsCanceledTask()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginFlush()
                .ReturnsCompletedSynchronously();
            innerStreamMock
                .SetupEndFlush()
                .Throws(new OperationCanceledException());
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task task = product.FlushAsync(cancellationToken);

            // Assert
            Assert.NotNull(task);
            Assert.Equal(TaskStatus.Canceled, task.Status);
        }

        [Fact]
        public void FlushAsync_WhenInnerStreamEndReadThrowsNonOperationCanceledException_ReturnsFaultedTask()
        {
            // Arrange
            Exception expectedException = new Exception();
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginFlush()
                .ReturnsCompletedSynchronously();
            innerStreamMock
                .SetupEndFlush()
                .Throws(expectedException);
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task task = product.FlushAsync(cancellationToken);

            // Assert
            Assert.NotNull(task);
            Assert.Equal(TaskStatus.Faulted, task.Status);
            Assert.Same(expectedException, task.Exception.InnerException);
        }

        [Fact]
        public void FlushAsync_CancelToken_WhenNotCompletedSynchronously_DelegatesToCancel()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            CancellableAsyncCompletionSource spy = new CancellableAsyncCompletionSource();
            innerStreamMock
                .SetupBeginFlush()
                .ReturnsCompletingAsynchronously(spy);
            innerStreamMock.SetupEndFlush();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            using (CancellationTokenSource tokenSource = new CancellationTokenSource())
            {
                CancellationToken cancellationToken = tokenSource.Token;

                Task task = product.FlushAsync(cancellationToken);
                Assert.NotNull(task); // Guard

                // Act
                tokenSource.Cancel();

                // Assert
                Assert.True(spy.Canceled);

                // Cleanup
                spy.Complete();
                task.GetAwaiter().GetResult();
            }
        }

        [Fact]
        public void FlushAsync_CancelToken_AfterCompletion_DoesNotCallCancellableAsyncResultCancel()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            CompletedCancellationSpy spy = new CompletedCancellationSpy();
            innerStreamMock
                .SetupBeginFlush()
                .ReturnsCompletedSynchronously(spy);
            innerStreamMock.SetupEndFlush();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            using (CancellationTokenSource tokenSource = new CancellationTokenSource())
            {
                CancellationToken cancellationToken = tokenSource.Token;

                product.FlushAsync(cancellationToken).GetAwaiter().GetResult();

                // Act
                tokenSource.Cancel();

                // Assert
                Assert.False(spy.Canceled);
            }
        }

        [Fact]
        public void FlushAsync_WhenNotCompletedSynchronously_CancelTokenDuringEndFlush_DoesNotCallAsyncResultCancel()
        {
            // Arrange
            using (CancellationTokenSource tokenSource = new CancellationTokenSource())
            {
                Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
                CancellableAsyncCompletionSource spy = new CancellableAsyncCompletionSource();
                innerStreamMock
                    .SetupBeginFlush()
                    .ReturnsCompletingAsynchronously(spy);
                innerStreamMock
                    .SetupEndFlush()
                    .Callback(() => tokenSource.Cancel());
                CloudBlobStream innerStream = innerStreamMock.Object;
                Stream product = CreateProductUnderTest(innerStream);
                CancellationToken cancellationToken = tokenSource.Token;

                Task task = product.FlushAsync(cancellationToken);
                Assert.NotNull(task); // Guard

                // Act
                spy.Complete();

                // Assert
                Assert.False(spy.Canceled);
                task.GetAwaiter().GetResult();
            }
        }

        [Fact]
        public void Read_DelegatesToInnerStreamRead()
        {
            byte[] expectedBuffer = new byte[0];
            int expectedOffset = 123;
            int expectedCount = 456;
            int expectedBytesRead = 789;

            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.Read(expectedBuffer, expectedOffset, expectedCount))
                .Returns(expectedBytesRead)
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            int bytesRead = product.Read(expectedBuffer, expectedOffset, expectedCount);

            // Assert
            Assert.Equal(expectedBytesRead, bytesRead);
            innerStreamMock.Verify();
        }

        [Fact]
        public void ReadAsync_DelegatesToInnerStreamReadAsync()
        {
            // Arrange
            byte[] expectedBuffer = new byte[0];
            int expectedOffset = 123;
            int expectedCount = 456;
            CancellationToken expectedCancellationToken = new CancellationToken(canceled: true);

            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.ReadAsync(expectedBuffer, expectedOffset, expectedCount, expectedCancellationToken))
                .Returns(Task.FromResult(-1))
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            product.ReadAsync(expectedBuffer, expectedOffset, expectedCount, expectedCancellationToken);

            // Assert
            innerStreamMock.Verify();
        }

        [Fact]
        public async Task ReadAsync_WhenInnerStreamThrows_PropogatesException()
        {
            // Arrange
            Exception expectedException = new Exception();
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Throws(expectedException);
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            CancellationToken cancellationToken = CancellationToken.None;

            // Act & Assert
            Exception exception = await Assert.ThrowsAsync<Exception>(
                () => product.ReadAsync(buffer, offset, count, cancellationToken));
            Assert.Same(expectedException, exception);
        }

        [Fact]
        public void ReadAsync_WhenInnerStreamHasNotYetCompleted_ReturnsIncompleteTask()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            TaskCompletionSource<int> taskSource = new TaskCompletionSource<int>();
            innerStreamMock
                .Setup(s => s.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(taskSource.Task);
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task<int> task = product.ReadAsync(buffer, offset, count, cancellationToken);

            // Assert
            Assert.NotNull(task);
            Assert.False(task.IsCompleted);
        }

        [Fact]
        public void ReadAsync_WhenInnerStreamHasCompleted_ReturnsRanToCompletionTask()
        {
            // Arrange
            int expectedBytesRead = 789;
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            TaskCompletionSource<int> taskSource = new TaskCompletionSource<int>();
            taskSource.SetResult(expectedBytesRead);
            innerStreamMock
                .Setup(s => s.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(taskSource.Task);
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task<int> task = product.ReadAsync(buffer, offset, count, cancellationToken);

            // Assert
            Assert.NotNull(task);
            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
            Assert.Equal(expectedBytesRead, task.Result);
        }

        [Fact]
        public void ReadAsync_WhenInnerStreamHasCanceled_ReturnsCanceledTask()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            TaskCompletionSource<int> taskSource = new TaskCompletionSource<int>();
            taskSource.SetCanceled();
            innerStreamMock
                .Setup(s => s.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(taskSource.Task);
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task<int> task = product.ReadAsync(buffer, offset, count, cancellationToken);

            // Assert
            Assert.NotNull(task);
            Assert.Equal(TaskStatus.Canceled, task.Status);
        }

        [Fact]
        public void ReadAsync_WhenInnerStreamHasFaulted_ReturnsFaultedTask()
        {
            // Arrange
            Exception expectedException = new Exception();
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            TaskCompletionSource<int> taskSource = new TaskCompletionSource<int>();
            taskSource.SetException(expectedException);
            innerStreamMock
                .Setup(s => s.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(taskSource.Task);
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task<int> task = product.ReadAsync(buffer, offset, count, cancellationToken);

            // Assert
            Assert.NotNull(task);
            Assert.Equal(TaskStatus.Faulted, task.Status);
            Assert.NotNull(task.Exception);
            Assert.Same(expectedException, task.Exception.InnerException);
        }

        [Fact]
        public void ReadByte_DelegatesToInnerStreamReadByte()
        {
            // Arrange
            int expected = 123;
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.ReadByte()).Returns(expected);
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            int actual = product.ReadByte();

            // Assert
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void Seek_DelegatesToInnerStreamSeek()
        {
            long expectedOffset = 123;
            SeekOrigin expectedOrigin = SeekOrigin.End;
            long expectedPosition = 456;

            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.Seek(expectedOffset, expectedOrigin))
                .Returns(expectedPosition)
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            long position = product.Seek(expectedOffset, expectedOrigin);

            // Assert
            Assert.Equal(expectedPosition, position);
            innerStreamMock.Verify();
        }

        [Fact]
        public void SetLength_DelegatesToInnerStreamSetLength()
        {
            long expectedValue = 123;

            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.SetLength(expectedValue))
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            product.SetLength(expectedValue);

            // Assert
            innerStreamMock.Verify();
        }

        [Fact]
        public void Write_DelegatesToInnerStreamWrite()
        {
            byte[] expectedBuffer = new byte[0];
            int expectedOffset = 123;
            int expectedCount = 456;

            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.Write(expectedBuffer, expectedOffset, expectedCount))
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            product.Write(expectedBuffer, expectedOffset, expectedCount);

            // Assert
            innerStreamMock.Verify();
        }

        [Fact]
        public void WriteAsync_DelegatesToInnerStreamWriteAsync()
        {
            // Arrange
            byte[] expectedBuffer = new byte[0];
            int expectedOffset = 123;
            int expectedCount = 456;
            CancellationToken expectedCancellationToken = new CancellationToken(canceled: true);

            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.WriteAsync(expectedBuffer, expectedOffset, expectedCount, expectedCancellationToken))
                .Returns(Task.FromResult(-1))
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            product.WriteAsync(expectedBuffer, expectedOffset, expectedCount, expectedCancellationToken);

            // Assert
            innerStreamMock.Verify();
        }

        [Fact]
        public async Task WriteAsync_WhenInnerStreamThrows_PropogatesException()
        {
            // Arrange
            Exception expectedException = new Exception();
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.WriteAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Throws(expectedException);
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            CancellationToken cancellationToken = CancellationToken.None;

            // Act & Assert
            Exception exception = await Assert.ThrowsAsync<Exception>(
                () => product.WriteAsync(buffer, offset, count, cancellationToken));
            Assert.Same(expectedException, exception);
        }

        [Fact]
        public void WriteAsync_WhenInnerStreamHasNotYetCompleted_ReturnsIncompleteTask()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            TaskCompletionSource<object> taskSource = new TaskCompletionSource<object>();
            innerStreamMock
                .Setup(s => s.WriteAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(taskSource.Task);
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task task = product.WriteAsync(buffer, offset, count, cancellationToken);

            // Assert
            Assert.NotNull(task);
            Assert.False(task.IsCompleted);
        }

        [Fact]
        public void WriteAsync_WhenInnerStreamHasCompleted_ReturnsRanToCompletionTask()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            TaskCompletionSource<object> taskSource = new TaskCompletionSource<object>();
            taskSource.SetResult(null);
            innerStreamMock
                .Setup(s => s.WriteAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(taskSource.Task);
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task task = product.WriteAsync(buffer, offset, count, cancellationToken);

            // Assert
            Assert.NotNull(task);
            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
        }

        [Fact]
        public void WriteAsync_WhenInnerStreamHasCanceled_ReturnsCanceledTask()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            TaskCompletionSource<int> taskSource = new TaskCompletionSource<int>();
            taskSource.SetCanceled();
            innerStreamMock
                .Setup(s => s.WriteAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(taskSource.Task);
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task task = product.WriteAsync(buffer, offset, count, cancellationToken);

            // Assert
            Assert.NotNull(task);
            Assert.Equal(TaskStatus.Canceled, task.Status);
        }

        [Fact]
        public void WriteAsync_WhenInnerStreamHasFaulted_ReturnsFaultedTask()
        {
            // Arrange
            Exception expectedException = new Exception();
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            TaskCompletionSource<int> taskSource = new TaskCompletionSource<int>();
            taskSource.SetException(expectedException);
            innerStreamMock
                .Setup(s => s.WriteAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(taskSource.Task);
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task task = product.WriteAsync(buffer, offset, count, cancellationToken);

            // Assert
            Assert.NotNull(task);
            Assert.Equal(TaskStatus.Faulted, task.Status);
            Assert.NotNull(task.Exception);
            Assert.Same(expectedException, task.Exception.InnerException);
        }

        [Fact]
        public void WriteByte_DelegatesToInnerStreamWriteByte()
        {
            // Arrange
            byte expected = 123;
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.WriteByte(expected))
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            product.WriteByte(expected);

            // Assert
            innerStreamMock.Verify();
        }

        // Be nicer than the SDK. Return false from CanWrite when calling Write would throw.

        [Fact]
        public void CanWrite_WhenCommitted_ReturnsFalse()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.CanWrite).Returns(true); // Emulate SDK
            innerStreamMock.Setup(s => s.Commit());
            CloudBlobStream innerStream = innerStreamMock.Object;
            CloudBlobStream product = CreateProductUnderTest(innerStream);
            product.Commit();

            // Act
            bool canWrite = product.CanWrite;

            // Assert
            Assert.False(canWrite);
        }

        [Fact]
        public void CanWrite_AfterBeginEndCommit_ReturnsFalse()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.CanWrite).Returns(true); // Emulate SDK
            innerStreamMock
                .SetupBeginCommit()
                .ReturnsCompletedSynchronously();
            innerStreamMock.SetupEndCommit();
            CloudBlobStream innerStream = innerStreamMock.Object;
            CloudBlobStream product = CreateProductUnderTest(innerStream);
            product.EndCommit(product.BeginCommit(null, null));

            // Act
            bool canWrite = product.CanWrite;

            // Assert
            Assert.False(canWrite);
        }

        [Fact]
        public void CanWrite_AfterCommitAsync_ReturnsFalse()
        {
            // Arrange

            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.CanWrite).Returns(true); // Emulate SDK
            innerStreamMock
                .SetupBeginCommit()
                .ReturnsCompletedSynchronously();
            innerStreamMock.SetupEndCommit();
            CloudBlobStream innerStream = innerStreamMock.Object;
            WatchableCloudBlobStream product = CreateProductUnderTest(innerStream);
            product.CommitAsync(CancellationToken.None).GetAwaiter().GetResult();

            // Act
            bool canWrite = product.CanWrite;

            // Assert
            Assert.False(canWrite);
        }

        [Fact]
        public void Close_WhenNotYetCommitted_Commits()
        {
            // Arrange
            bool committed = false;
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.Close());
            innerStreamMock.Setup(s => s.Commit()).Callback(() => committed = true);
            CloudBlobStream innerStream = innerStreamMock.Object;
            CloudBlobStream product = CreateProductUnderTest(innerStream);

            // Act
            product.Close();

            // Assert
            Assert.True(committed);
        }

        [Fact]
        public void Close_WhenAlreadyCommitted_DoesNotCommitAgain()
        {
            // Arrange
            int commitCalls = 0;
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.Close());
            innerStreamMock.Setup(s => s.Commit()).Callback(() => commitCalls++);
            CloudBlobStream innerStream = innerStreamMock.Object;
            CloudBlobStream product = CreateProductUnderTest(innerStream);
            product.Commit();
            Assert.Equal(1, commitCalls); // Guard

            // Act
            product.Close();

            // Assert
            Assert.Equal(1, commitCalls);
        }

        [Fact]
        public void Close_WhenInnerStreamCommitThrewOnPreviousCommit_DoesNotTryToCommitAgain()
        {
            // Arrange
            int commitCalls = 0;
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.Close());
            InvalidOperationException expectedException = new InvalidOperationException();
            innerStreamMock.Setup(s => s.Commit()).Callback(() => commitCalls++).Throws(expectedException);
            CloudBlobStream innerStream = innerStreamMock.Object;
            CloudBlobStream product = CreateProductUnderTest(innerStream);
            InvalidOperationException committedActionException = Assert.Throws<InvalidOperationException>(
                () => product.Commit()); // Guard
            Assert.Same(expectedException, committedActionException); // Guard
            Assert.Equal(1, commitCalls); // Guard

            // Act
            product.Close();

            // Assert
            Assert.Equal(1, commitCalls);
        }

        [Fact]
        public void Close_WhenInnerStreamBeginCommitThrewOnPreviousCommitAsync_DoesNotTryToCommitAgain()
        {
            // Arrange
            int commitCalls = 0;
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.Close());
            InvalidOperationException expectedException = new InvalidOperationException();
            innerStreamMock
                .SetupBeginCommit()
                .Callback(() => commitCalls++)
                .Throws(expectedException);
            innerStreamMock.SetupEndCommit();
            innerStreamMock.Setup(s => s.Commit()).Callback(() => commitCalls++);
            CloudBlobStream innerStream = innerStreamMock.Object;
            WatchableCloudBlobStream product = CreateProductUnderTest(innerStream);
            InvalidOperationException committedActionException = Assert.Throws<InvalidOperationException>(
                () => product.CommitAsync(CancellationToken.None).GetAwaiter().GetResult()); // Guard
            Assert.Same(expectedException, committedActionException); // Guard
            Assert.Equal(1, commitCalls); // Guard

            // Act
            product.Close();

            // Assert
            Assert.Equal(1, commitCalls);
        }

        [Fact]
        public void Close_WhenInnerStreamBeginCommitThrewOnPreviousBeginCommit_DoesNotTryToCommitAgain()
        {
            // Arrange
            int commitCalls = 0;
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.Close());
            InvalidOperationException expectedException = new InvalidOperationException();
            innerStreamMock
                .SetupBeginCommit()
                .Callback(() => commitCalls++)
                .Throws(expectedException);
            innerStreamMock.SetupEndCommit();
            innerStreamMock.Setup(s => s.Commit()).Callback(() => commitCalls++);
            CloudBlobStream innerStream = innerStreamMock.Object;
            WatchableCloudBlobStream product = CreateProductUnderTest(innerStream);
            InvalidOperationException committedActionException = Assert.Throws<InvalidOperationException>(
                () => product.BeginCommit(callback: null, state: null)); // Guard
            Assert.Same(expectedException, committedActionException); // Guard
            Assert.Equal(1, commitCalls); // Guard

            // Act
            product.Close();

            // Assert
            Assert.Equal(1, commitCalls);
        }

        [Fact]
        public void Commit_IfCommittedActionIsNotNull_CallsCommittedAction()
        {
            // Arrange
            Mock<IBlobCommitedAction> committedActionMock = new Mock<IBlobCommitedAction>(MockBehavior.Strict);
            committedActionMock
                .Setup(a => a.ExecuteAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(0))
                .Verifiable();
            IBlobCommitedAction committedAction = committedActionMock.Object;

            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.Commit());
            CloudBlobStream innerStream = innerStreamMock.Object;

            CloudBlobStream product = CreateProductUnderTest(innerStream, committedAction);

            // Act
            product.Commit();

            // Assert
            committedActionMock.Verify();
        }

        [Fact]
        public void Commit_IfCommitedActionIsNull_DoesNotThrow()
        {
            // Arrange
            IBlobCommitedAction committedAction = null;
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.Commit());
            CloudBlobStream innerStream = innerStreamMock.Object;
            CloudBlobStream product = CreateProductUnderTest(innerStream, committedAction);

            // Act & Assert
            product.Commit();
        }

        [Fact]
        public void CommitAsync_DelegatesToInnerStreamBeginEndCommit()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            CancellableAsyncCompletionSource completion = new CancellableAsyncCompletionSource();
            innerStreamMock
                .SetupBeginCommit()
                .ReturnsCompletingAsynchronously(completion)
                .Verifiable();
            innerStreamMock
                .Setup(s => s.EndCommit(It.Is<IAsyncResult>((ar) => ar == completion.AsyncResult)))
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            WatchableCloudBlobStream product = CreateProductUnderTest(innerStream);

            CancellationToken cancellationToken = CancellationToken.None;
            Task task = product.CommitAsync(cancellationToken);

            // Act
            completion.Complete();

            // Assert
            innerStreamMock.Verify();
            task.GetAwaiter().GetResult();
        }

        [Fact]
        public async Task CommitAsync_WhenInnerStreamBeginCommitThrows_PropogatesException()
        {
            // Arrange
            Exception expectedException = new Exception();
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginCommit()
                .Throws(expectedException);
            CloudBlobStream innerStream = innerStreamMock.Object;
            WatchableCloudBlobStream product = CreateProductUnderTest(innerStream);

            CancellationToken cancellationToken = CancellationToken.None;

            // Act & Assert
            Exception exception = await Assert.ThrowsAsync<Exception>(() => product.CommitAsync(cancellationToken));
            Assert.Same(expectedException, exception);
        }

        [Fact]
        public void CommitAsync_WhenInnerStreamBeginCommitHasNotYetCompleted_ReturnsIncompleteTask()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginCommit()
                .ReturnsUncompleted();
            CloudBlobStream innerStream = innerStreamMock.Object;
            WatchableCloudBlobStream product = CreateProductUnderTest(innerStream);

            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task task = product.CommitAsync(cancellationToken);

            // Assert
            Assert.NotNull(task);
            Assert.False(task.IsCompleted);
        }

        [Fact]
        public void CommitAsync_WhenInnerStreamBeginCommitCompletedSynchronously_ReturnsRanToCompletionTask()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginCommit()
                .ReturnsCompletedSynchronously();
            innerStreamMock.SetupEndCommit();
            CloudBlobStream innerStream = innerStreamMock.Object;
            WatchableCloudBlobStream product = CreateProductUnderTest(innerStream);

            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task task = product.CommitAsync(cancellationToken);

            // Assert
            Assert.NotNull(task);
            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
        }

        [Fact]
        public void CommitAsync_WhenInnerStreamBeginCommitCompletedAsynchronously_ReturnsRanToCompletionTask()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            CancellableAsyncCompletionSource completion = new CancellableAsyncCompletionSource();
            innerStreamMock
                .SetupBeginCommit()
                .ReturnsCompletingAsynchronously(completion);
            innerStreamMock.SetupEndCommit();
            CloudBlobStream innerStream = innerStreamMock.Object;
            WatchableCloudBlobStream product = CreateProductUnderTest(innerStream);

            CancellationToken cancellationToken = CancellationToken.None;

            Task task = product.CommitAsync(cancellationToken);

            // Act
            completion.Complete();

            // Assert
            Assert.NotNull(task);
            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
        }

        [Fact]
        public void CommitAsync_WhenInnerStreamEndReadThrowsOperationCanceledException_ReturnsCanceledTask()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginCommit()
                .ReturnsCompletedSynchronously();
            innerStreamMock
                .SetupEndCommit()
                .Throws(new OperationCanceledException());
            CloudBlobStream innerStream = innerStreamMock.Object;
            WatchableCloudBlobStream product = CreateProductUnderTest(innerStream);

            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task task = product.CommitAsync(cancellationToken);

            // Assert
            Assert.NotNull(task);
            Assert.Equal(TaskStatus.Canceled, task.Status);
        }

        [Fact]
        public void CommitAsync_WhenInnerStreamEndReadThrowsNonOperationCanceledException_ReturnsFaultedTask()
        {
            // Arrange
            Exception expectedException = new Exception();
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginCommit()
                .ReturnsCompletedSynchronously();
            innerStreamMock
                .SetupEndCommit()
                .Throws(expectedException);
            CloudBlobStream innerStream = innerStreamMock.Object;
            WatchableCloudBlobStream product = CreateProductUnderTest(innerStream);

            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task task = product.CommitAsync(cancellationToken);

            // Assert
            Assert.NotNull(task);
            Assert.Equal(TaskStatus.Faulted, task.Status);
            Assert.Same(expectedException, task.Exception.InnerException);
        }

        [Fact]
        public void CommitAsync_CancelToken_WhenNotCompletedSynchronously_DelegatesToCancel()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            CancellableAsyncCompletionSource spy = new CancellableAsyncCompletionSource();
            innerStreamMock
                .SetupBeginCommit()
                .ReturnsCompletingAsynchronously(spy);
            innerStreamMock.SetupEndCommit();
            CloudBlobStream innerStream = innerStreamMock.Object;
            WatchableCloudBlobStream product = CreateProductUnderTest(innerStream);

            using (CancellationTokenSource tokenSource = new CancellationTokenSource())
            {
                CancellationToken cancellationToken = tokenSource.Token;

                Task task = product.CommitAsync(cancellationToken);
                Assert.NotNull(task); // Guard

                // Act
                tokenSource.Cancel();

                // Assert
                Assert.True(spy.Canceled);

                // Cleanup
                spy.Complete();
                task.GetAwaiter().GetResult();
            }
        }

        [Fact]
        public void CommitAsync_CancelToken_AfterCompletion_DoesNotCallCancellableAsyncResultCancel()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            CompletedCancellationSpy spy = new CompletedCancellationSpy();
            innerStreamMock
                .SetupBeginCommit()
                .ReturnsCompletedSynchronously(spy);
            innerStreamMock.SetupEndCommit();
            CloudBlobStream innerStream = innerStreamMock.Object;
            WatchableCloudBlobStream product = CreateProductUnderTest(innerStream);

            using (CancellationTokenSource tokenSource = new CancellationTokenSource())
            {
                CancellationToken cancellationToken = tokenSource.Token;

                Task task = product.CommitAsync(cancellationToken);
                Assert.NotNull(task); // Guard

                // Act
                tokenSource.Cancel();

                // Assert
                Assert.False(spy.Canceled);
                task.GetAwaiter().GetResult();
            }
        }

        [Fact]
        public void CommitAsync_WhenNotCompletedSynchronously_CancelTokenDuringEndCommit_DoesNotCallAsyncResultCancel()
        {
            // Arrange
            using (CancellationTokenSource tokenSource = new CancellationTokenSource())
            {
                Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
                CancellableAsyncCompletionSource spy = new CancellableAsyncCompletionSource();
                innerStreamMock
                    .SetupBeginCommit()
                    .ReturnsCompletingAsynchronously(spy);
                innerStreamMock
                    .SetupEndCommit()
                    .Callback(() => tokenSource.Cancel());
                CloudBlobStream innerStream = innerStreamMock.Object;
                WatchableCloudBlobStream product = CreateProductUnderTest(innerStream);
                CancellationToken cancellationToken = tokenSource.Token;

                Task task = product.CommitAsync(cancellationToken);
                Assert.NotNull(task); // Guard

                // Act
                spy.Complete();

                // Assert
                Assert.False(spy.Canceled);
                task.GetAwaiter().GetResult();
            }
        }

        [Fact]
        public void CommitAsync_IfCommittedActionIsNotNull_CallsCommittedAction()
        {
            // Arrange
            CancellationToken expectedCancellationToken = new CancellationToken(canceled: true);
            Mock<IBlobCommitedAction> committedActionMock = new Mock<IBlobCommitedAction>(MockBehavior.Strict);
            committedActionMock
                .Setup(a => a.ExecuteAsync(expectedCancellationToken))
                .Returns(Task.FromResult(0))
                .Verifiable();
            IBlobCommitedAction committedAction = committedActionMock.Object;

            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginCommit()
                .ReturnsCompletedSynchronously();
            innerStreamMock.SetupEndCommit();
            CloudBlobStream innerStream = innerStreamMock.Object;

            WatchableCloudBlobStream product = CreateProductUnderTest(innerStream, committedAction);

            // Act
            product.CommitAsync(expectedCancellationToken).GetAwaiter().GetResult();

            // Assert
            committedActionMock.Verify();
        }

        [Fact]
        public void CommitAsync_IfCommitedActionIsNull_DoesNotThrow()
        {
            // Arrange
            IBlobCommitedAction committedAction = null;
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginCommit()
                .ReturnsCompletedSynchronously();
            innerStreamMock.SetupEndCommit();
            CloudBlobStream innerStream = innerStreamMock.Object;
            WatchableCloudBlobStream product = CreateProductUnderTest(innerStream, committedAction);

            // Act & Assert
            product.CommitAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        [Fact]
        public void CompleteAsync_WhenChanged_DelegatesToBeginEndCommit()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            CompletedCancellationSpy spy = new CompletedCancellationSpy();
            innerStreamMock.Setup(s => s.WriteByte(It.IsAny<byte>()));
            innerStreamMock
                .SetupBeginCommit()
                .ReturnsCompletedSynchronously(spy)
                .Verifiable();
            innerStreamMock
                .Setup(s => s.EndCommit(It.Is<IAsyncResult>((ar) => ar == spy.AsyncResult)))
                .Verifiable();
            CloudBlobStream innerStream = innerStreamMock.Object;
            WatchableCloudBlobStream product = CreateProductUnderTest(innerStream);

            product.WriteByte(0x00);

            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task task = product.CompleteAsync(cancellationToken);

            // Assert
            innerStreamMock.Verify();
            Assert.NotNull(task);
            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
        }

        [Fact]
        public void CompleteAsync_WhenChangedAndCancellationIsRequested_CallsCancellableAsyncResultCancel()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            CancellableAsyncCompletionSource spy = new CancellableAsyncCompletionSource();
            innerStreamMock.Setup(s => s.WriteByte(It.IsAny<byte>()));
            innerStreamMock
                .SetupBeginCommit()
                .ReturnsCompletingAsynchronously(spy);
            innerStreamMock.SetupEndCommit();
            CloudBlobStream innerStream = innerStreamMock.Object;
            WatchableCloudBlobStream product = CreateProductUnderTest(innerStream);

            product.WriteByte(0x00);

            CancellationToken cancellationToken = new CancellationToken(canceled: true);
            Task task = product.CompleteAsync(cancellationToken);

            // Act
            spy.Complete();

            // Assert
            Assert.True(spy.Canceled);
            Assert.NotNull(task);
            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
        }

        [Fact]
        public void CompleteAsync_WhenChangedAndCommitted_DoesNotCommitAgainButStillReturnsTrue()
        {
            // Arrange
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            CancellableAsyncCompletionSource spy = new CancellableAsyncCompletionSource();
            bool committedAgain = false;
            innerStreamMock.Setup(s => s.WriteByte(It.IsAny<byte>()));
            innerStreamMock.Setup(s => s.Commit());
            innerStreamMock
                .SetupBeginCommit()
                .Callback(() => committedAgain = true)
                .ReturnsCompletedSynchronously();
            innerStreamMock.SetupEndCommit();
            CloudBlobStream innerStream = innerStreamMock.Object;
            WatchableCloudBlobStream product = CreateProductUnderTest(innerStream);

            product.WriteByte(0x00);
            product.Commit();

            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task<bool> task = product.CompleteAsync(cancellationToken);

            // Assert
            Assert.False(committedAgain);
            Assert.NotNull(task);
            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
            Assert.True(task.Result);
        }

        [Fact]
        public void CompleteAsync_WhenUnchanged_DoesNotCommitAndReturnsFalse()
        {
            // Arrange
            bool committed = false;
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            CancellableAsyncCompletionSource spy = new CancellableAsyncCompletionSource();
            innerStreamMock
                .SetupBeginCommit()
                .Callback(() => committed = true)
                .ReturnsCompletedSynchronously();
            innerStreamMock.SetupEndCommit();
            CloudBlobStream innerStream = innerStreamMock.Object;
            WatchableCloudBlobStream product = CreateProductUnderTest(innerStream);

            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task<bool> task = product.CompleteAsync(cancellationToken);

            // Assert
            Assert.False(committed);
            Assert.NotNull(task);
            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
            Assert.False(task.Result);
        }

        [Fact]
        public void CompleteAsync_AfterFlush_CommitsAndReturnsTrue()
        {
            // Arrange
            bool committed = false;
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.Flush());
            innerStreamMock
                .SetupBeginCommit()
                .Callback(() => committed = true)
                .ReturnsCompletedSynchronously();
            innerStreamMock.SetupEndCommit();
            CloudBlobStream innerStream = innerStreamMock.Object;
            WatchableCloudBlobStream product = CreateProductUnderTest(innerStream);

            product.Flush();

            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task<bool> task = product.CompleteAsync(cancellationToken);

            // Assert
            Assert.True(committed);
            Assert.NotNull(task);
            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
            Assert.True(task.Result);
        }

        [Fact]
        public void CompleteAsync_AfterBeginEndFlush_CommitsAndReturnsTrue()
        {
            // Arrange
            bool committed = false;
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginFlush()
                .ReturnsCompletedSynchronously();
            innerStreamMock.SetupEndFlush();
            innerStreamMock
                .SetupBeginCommit()
                .Callback(() => committed = true)
                .ReturnsCompletedSynchronously();
            innerStreamMock.SetupEndCommit();
            CloudBlobStream innerStream = innerStreamMock.Object;
            WatchableCloudBlobStream product = CreateProductUnderTest(innerStream);

            product.EndFlush(product.BeginFlush(null, null));

            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task<bool> task = product.CompleteAsync(cancellationToken);

            // Assert
            Assert.True(committed);
            Assert.NotNull(task);
            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
            Assert.True(task.Result);
        }

        [Fact]
        public void CompleteAsync_AfterFlushAsync_CommitsAndReturnsTrue()
        {
            // Arrange
            bool committed = false;
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginFlush()
                .ReturnsCompletedSynchronously();
            innerStreamMock.SetupEndFlush();
            innerStreamMock
                .SetupBeginCommit()
                .Callback(() => committed = true)
                .ReturnsCompletedSynchronously();
            innerStreamMock.SetupEndCommit();
            CloudBlobStream innerStream = innerStreamMock.Object;
            WatchableCloudBlobStream product = CreateProductUnderTest(innerStream);

            product.FlushAsync().GetAwaiter().GetResult();

            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task<bool> task = product.CompleteAsync(cancellationToken);

            // Assert
            Assert.True(committed);
            Assert.NotNull(task);
            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
            Assert.True(task.Result);
        }

        [Fact]
        public void CompleteAsync_AfterWrite_CommitsAndReturnsTrue()
        {
            // Arrange
            bool committed = false;
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.Write(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()));
            innerStreamMock
                .SetupBeginCommit()
                .Callback(() => committed = true)
                .ReturnsCompletedSynchronously();
            innerStreamMock.SetupEndCommit();
            CloudBlobStream innerStream = innerStreamMock.Object;
            WatchableCloudBlobStream product = CreateProductUnderTest(innerStream);

            product.Write(new byte[] { 0x00 }, 0, 1);

            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task<bool> task = product.CompleteAsync(cancellationToken);

            // Assert
            Assert.True(committed);
            Assert.NotNull(task);
            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
            Assert.True(task.Result);
        }

        [Fact]
        public void CompleteAsync_AfterBeginEndWrite_CommitsAndReturnsTrue()
        {
            // Arrange
            bool committed = false;
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .SetupBeginWrite()
                .ReturnsCompletedSynchronously();
            innerStreamMock.SetupEndWrite();
            innerStreamMock
                .SetupBeginCommit()
                .Callback(() => committed = true)
                .ReturnsCompletedSynchronously();
            innerStreamMock.SetupEndCommit();
            CloudBlobStream innerStream = innerStreamMock.Object;
            WatchableCloudBlobStream product = CreateProductUnderTest(innerStream);

            product.EndWrite(product.BeginWrite(new byte[] { 0x00 }, 0, 1, null, null));

            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task<bool> task = product.CompleteAsync(cancellationToken);

            // Assert
            Assert.True(committed);
            Assert.NotNull(task);
            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
            Assert.True(task.Result);
        }

        [Fact]
        public void CompleteAsync_AfterWriteAsync_CommitsAndReturnsTrue()
        {
            // Arrange
            bool committed = false;
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.WriteAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(0));
            innerStreamMock
                .SetupBeginCommit()
                .Callback(() => committed = true)
                .ReturnsCompletedSynchronously();
            innerStreamMock.SetupEndCommit();
            CloudBlobStream innerStream = innerStreamMock.Object;
            WatchableCloudBlobStream product = CreateProductUnderTest(innerStream);

            product.WriteAsync(new byte[] { 0x00 }, 0, 1).GetAwaiter().GetResult();

            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task<bool> task = product.CompleteAsync(cancellationToken);

            // Assert
            Assert.True(committed);
            Assert.NotNull(task);
            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
            Assert.True(task.Result);
        }

        [Fact]
        public void CompleteAsync_AfterWriteByte_CommitsAndReturnsTrue()
        {
            // Arrange
            bool committed = false;
            Mock<CloudBlobStream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.WriteByte(It.IsAny<byte>()));
            innerStreamMock
                .SetupBeginCommit()
                .Callback(() => committed = true)
                .ReturnsCompletedSynchronously();
            innerStreamMock.SetupEndCommit();
            CloudBlobStream innerStream = innerStreamMock.Object;
            WatchableCloudBlobStream product = CreateProductUnderTest(innerStream);

            product.WriteByte(0x00);

            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task<bool> task = product.CompleteAsync(cancellationToken);

            // Assert
            Assert.True(committed);
            Assert.NotNull(task);
            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
            Assert.True(task.Result);
        }

        [Fact]
        public void GetStatus_Initially_ReturnsNull()
        {
            // Arrange
            using (CloudBlobStream innerStream = CreateInnerStream())
            using (WatchableCloudBlobStream product = CreateProductUnderTest(innerStream))
            {
                // Act
                ParameterLog status = product.GetStatus();

                // Assert
                Assert.Null(status);
            }
        }

        [Fact]
        public void GetStatus_AfterCommit_ReturnsZeroBytesWritten()
        {
            // Arrange
            using (CloudBlobStream innerStream = CreateInnerStream())
            using (WatchableCloudBlobStream product = CreateProductUnderTest(innerStream))
            {
                product.Commit();

                // Act
                ParameterLog status = product.GetStatus();

                // Assert
                AssertEqualStatus(0, status);
            }
        }

        [Fact]
        public void GetStatus_AfterFlush_ReturnsZeroBytesWritten()
        {
            // Arrange
            using (CloudBlobStream innerStream = CreateInnerStream())
            using (WatchableCloudBlobStream product = CreateProductUnderTest(innerStream))
            {
                byte[] buffer = Encoding.UTF8.GetBytes("abc");
                product.Flush();

                // Act
                ParameterLog status = product.GetStatus();

                // Assert
                AssertEqualStatus(0, status);
            }
        }

        [Fact]
        public void GetStatus_AfterWrite_ReturnsBytesWritten()
        {
            // Arrange
            using (CloudBlobStream innerStream = CreateInnerStream())
            using (WatchableCloudBlobStream product = CreateProductUnderTest(innerStream))
            {
                byte[] buffer = Encoding.UTF8.GetBytes("abc");
                product.Write(buffer, 0, buffer.Length);

                // Act
                ParameterLog status = product.GetStatus();

                // Assert
                AssertEqualStatus(buffer.Length, status);
            }
        }

        [Fact]
        public void GetStatus_AfterWriteTwice_ReturnsTotalBytesWritten()
        {
            // Arrange
            using (CloudBlobStream innerStream = CreateInnerStream())
            using (WatchableCloudBlobStream product = CreateProductUnderTest(innerStream))
            {
                byte[] buffer = Encoding.UTF8.GetBytes("abc");
                product.Write(buffer, 0, buffer.Length);
                product.Write(buffer, 0, buffer.Length);

                // Act
                ParameterLog status = product.GetStatus();

                // Assert
                AssertEqualStatus(buffer.Length * 2, status);
            }
        }

        [Fact]
        public void GetStatus_AfterBeginEndWrite_ReturnsBytesWritten()
        {
            // Arrange
            using (CloudBlobStream innerStream = CreateInnerStream())
            using (WatchableCloudBlobStream product = CreateProductUnderTest(innerStream))
            {
                byte[] buffer = Encoding.UTF8.GetBytes("abc");
                product.EndWrite(product.BeginWrite(buffer, 0, buffer.Length, null, null));

                // Act
                ParameterLog status = product.GetStatus();

                // Assert
                AssertEqualStatus(buffer.Length, status);
            }
        }

        [Fact]
        public void GetStatus_AfterBeginEndWriteTwice_ReturnsTotalBytesWritten()
        {
            // Arrange
            using (CloudBlobStream innerStream = CreateInnerStream())
            using (WatchableCloudBlobStream product = CreateProductUnderTest(innerStream))
            {
                byte[] buffer = Encoding.UTF8.GetBytes("abc");
                product.EndWrite(product.BeginWrite(buffer, 0, buffer.Length, null, null));
                product.EndWrite(product.BeginWrite(buffer, 0, buffer.Length, null, null));

                // Act
                ParameterLog status = product.GetStatus();

                // Assert
                AssertEqualStatus(buffer.Length * 2, status);
            }
        }

        [Fact]
        public void GetStatus_AfterWriteAsync_ReturnsBytesWritten()
        {
            // Arrange
            using (CloudBlobStream innerStream = CreateInnerStream())
            using (WatchableCloudBlobStream product = CreateProductUnderTest(innerStream))
            {
                byte[] buffer = Encoding.UTF8.GetBytes("abc");
                product.WriteAsync(buffer, 0, buffer.Length).GetAwaiter().GetResult();

                // Act
                ParameterLog status = product.GetStatus();

                // Assert
                AssertEqualStatus(buffer.Length, status);
            }
        }

        [Fact]
        public void GetStatus_AfterWriteAsyncTwice_ReturnsTotalBytesWritten()
        {
            // Arrange
            using (CloudBlobStream innerStream = CreateInnerStream())
            using (WatchableCloudBlobStream product = CreateProductUnderTest(innerStream))
            {
                byte[] buffer = Encoding.UTF8.GetBytes("abc");
                product.WriteAsync(buffer, 0, buffer.Length).GetAwaiter().GetResult();
                product.WriteAsync(buffer, 0, buffer.Length).GetAwaiter().GetResult();

                // Act
                ParameterLog status = product.GetStatus();

                // Assert
                AssertEqualStatus(buffer.Length * 2, status);
            }
        }

        [Fact]
        public void GetStatus_AfterWriteByte_ReturnsOneByteWritten()
        {
            // Arrange
            using (CloudBlobStream innerStream = CreateInnerStream())
            using (WatchableCloudBlobStream product = CreateProductUnderTest(innerStream))
            {
                product.WriteByte(0xff);

                // Act
                ParameterLog status = product.GetStatus();

                // Assert
                AssertEqualStatus(1, status);
            }
        }

        [Fact]
        public void GetStatus_AfterWriteByteTwice_ReturnsTwoBytesWritten()
        {
            // Arrange
            using (CloudBlobStream innerStream = CreateInnerStream())
            using (WatchableCloudBlobStream product = CreateProductUnderTest(innerStream))
            {
                product.WriteByte(0xff);
                product.WriteByte(0xff);

                // Act
                ParameterLog status = product.GetStatus();

                // Assert
                AssertEqualStatus(2, status);
            }
        }

        [Fact]
        public void GetStatus_AfterCompleteAsyncWhenNotChanged_ReturnsNotWritten()
        {
            // Arrange
            using (CloudBlobStream innerStream = CreateInnerStream())
            using (WatchableCloudBlobStream product = CreateProductUnderTest(innerStream))
            {
                bool committed = product.CompleteAsync(CancellationToken.None).GetAwaiter().GetResult();
                Assert.False(committed); // Guard

                // Act
                ParameterLog status = product.GetStatus();

                // Assert
                AssertNotWritten(status);
            }
        }

        [Fact]
        public void GetStatus_AfterCompleteAsyncWhenChanged_ReturnsBytesWritten()
        {
            // Arrange
            using (CloudBlobStream innerStream = CreateInnerStream())
            using (WatchableCloudBlobStream product = CreateProductUnderTest(innerStream))
            {
                product.WriteByte(0xff);
                bool committed = product.CompleteAsync(CancellationToken.None).GetAwaiter().GetResult();
                Assert.True(committed); // Guard

                // Act
                ParameterLog status = product.GetStatus();

                // Assert
                AssertEqualStatus(1, status);
            }
        }

        private static void AssertEqual(ExpectedAsyncResult expected, IAsyncResult actual,
            bool disposeActual = false)
        {
            Assert.NotNull(actual);
            Assert.Same(expected.AsyncState, actual.AsyncState);
            Assert.Equal(expected.CompletedSynchronously, actual.CompletedSynchronously);
            Assert.Equal(expected.IsCompleted, actual.IsCompleted);

            try
            {
                Assert.Equal(expected.IsCompleted, actual.AsyncWaitHandle.WaitOne(0));
            }
            finally
            {
                if (disposeActual)
                {
                    actual.Dispose();
                }
            }
        }

        private static void AssertEqualStatus(int expectedBytesWritted, ParameterLog actual)
        {
            Assert.IsType<WriteBlobParameterLog>(actual);
            WriteBlobParameterLog actualBlobLog = (WriteBlobParameterLog)actual;
            Assert.Equal(expectedBytesWritted, actualBlobLog.BytesWritten);
            Assert.True(actualBlobLog.WasWritten);
        }

        private static void AssertNotWritten(ParameterLog actual)
        {
            Assert.IsType<WriteBlobParameterLog>(actual);
            WriteBlobParameterLog actualBlobLog = (WriteBlobParameterLog)actual;
            Assert.False(actualBlobLog.WasWritten);
            Assert.Equal(0, actualBlobLog.BytesWritten);
        }

        private static Stream CreateDummyStream()
        {
            return new Mock<Stream>(MockBehavior.Strict).Object;
        }

        private static CloudBlobStream CreateInnerStream()
        {
            return new FakeCloudBlobStream(new MemoryStream());
        }

        private static Mock<CloudBlobStream> CreateMockInnerStream()
        {
            return new Mock<CloudBlobStream>(MockBehavior.Strict);
        }

        private static WatchableCloudBlobStream CreateProductUnderTest(CloudBlobStream inner)
        {
            return CreateProductUnderTest(inner, NullBlobCommittedAction.Instance);
        }

        private static WatchableCloudBlobStream CreateProductUnderTest(CloudBlobStream inner,
            IBlobCommitedAction committedAction)
        {
            return new WatchableCloudBlobStream(inner, committedAction);
        }

        private struct ExpectedAsyncResult
        {
            public object AsyncState;
            public bool CompletedSynchronously;
            public bool IsCompleted;
        }

        private class NullBlobCommittedAction : IBlobCommitedAction
        {
            private static readonly NullBlobCommittedAction _instance = new NullBlobCommittedAction();

            private NullBlobCommittedAction()
            {
            }

            public static NullBlobCommittedAction Instance
            {
                get { return _instance; }
            }

            public Task ExecuteAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(0);
            }
        }
    }
}
