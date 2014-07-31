// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Blobs;
using Microsoft.Azure.Jobs.Host.Protocols;
using Moq;
using Xunit;
using Xunit.Extensions;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Blobs
{
    public class WatchableReadStreamTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CanRead_DelegatesToInnerStreamCanRead(bool expected)
        {
            // Arrange
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.CanRead).Returns(expected);
            Stream innerStream = innerStreamMock.Object;
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
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.CanSeek).Returns(expected);
            Stream innerStream = innerStreamMock.Object;
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
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.CanTimeout).Returns(expected);
            Stream innerStream = innerStreamMock.Object;
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
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.CanWrite).Returns(expected);
            Stream innerStream = innerStreamMock.Object;
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
            Mock<Stream> innerStreamMock = new Mock<Stream>(MockBehavior.Strict);
            innerStreamMock.Setup(s => s.Length).Returns(expected);
            Stream innerStream = innerStreamMock.Object;
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
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.Position).Returns(expected);
            Stream innerStream = innerStreamMock.Object;
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
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.SetupSet(s => s.Position = expected).Verifiable();
            Stream innerStream = innerStreamMock.Object;
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
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.ReadTimeout).Returns(expected);
            Stream innerStream = innerStreamMock.Object;
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
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.SetupSet(s => s.ReadTimeout = expected).Verifiable();
            Stream innerStream = innerStreamMock.Object;
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
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.WriteTimeout).Returns(expected);
            Stream innerStream = innerStreamMock.Object;
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
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.SetupSet(s => s.WriteTimeout = expected).Verifiable();
            Stream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            product.WriteTimeout = expected;

            // Assert
            innerStreamMock.Verify();
        }

        [Fact]
        public void BeginRead_DelegatesToInnerStreamBeginRead()
        {
            // Arrange
            byte[] expectedBuffer = new byte[0];
            int expectedOffset = 123;
            int expectedCount = 456;

            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.BeginRead(expectedBuffer, expectedOffset, expectedCount, It.IsAny<AsyncCallback>(),
                    It.IsAny<object>()))
                .Returns(CreateStubAsyncResult)
                .Verifiable();
            Stream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            AsyncCallback callback = (_) => { };
            object state = new object();

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
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.BeginRead(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<AsyncCallback>(),
                    It.IsAny<object>()))
                .Throws(expectedException);
            Stream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            AsyncCallback callback = (_) => { };
            object state = new object();

            // Act & Assert
            Exception exception = Assert.Throws<Exception>(() => product.BeginRead(buffer, offset, count, callback,
                state));
            Assert.Same(expectedException, exception);
        }

        [Fact]
        public void BeginRead_WhenNotYetCompleted_ReturnsUncompletedResult()
        {
            // Arrange
            using (EventWaitHandle asyncWaitHandle = new ManualResetEvent(initialState: false))
            {
                Mock<Stream> innerStreamMock = CreateMockInnerStream();
                innerStreamMock
                    .Setup(s => s.BeginRead(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                        It.IsAny<AsyncCallback>(), It.IsAny<object>()))
                    .Returns<byte[], int, int, AsyncCallback, object>((i1, i2, i3, innerCallback, innerState) =>
                        CreateAsyncResult(innerState, asyncWaitHandle, completedSynchronously: false));
                Stream innerStream = innerStreamMock.Object;
                Stream product = CreateProductUnderTest(innerStream);

                byte[] buffer = new byte[0];
                int offset = 123;
                int count = 456;
                AsyncCallback callback = (_) => { };
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
                AssertEqual(expectedResult, result, disposeWaitHandle: true);
            }
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
                callbackCalled = true;
                callbackResult = ar;
                AssertEqual(expectedResult, ar);
            };

            using (EventWaitHandle asyncWaitHandle = new ManualResetEvent(initialState: true))
            {
                Mock<Stream> innerStreamMock = CreateMockInnerStream();
                IAsyncResult innerResult = null;
                innerStreamMock
                    .Setup(s => s.BeginRead(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                        It.IsAny<AsyncCallback>(), It.IsAny<object>()))
                    .Returns<byte[], int, int, AsyncCallback, object>((i1, i2, i3, innerCallback, innerState) =>
                        {
                            innerResult = CreateAsyncResult(innerState, asyncWaitHandle, completedSynchronously: true);
                            innerCallback(innerResult);
                            return innerResult;
                        });
                innerStreamMock
                    .Setup(s => s.EndRead(It.IsAny<IAsyncResult>()))
                    .Returns(-1);
                Stream innerStream = innerStreamMock.Object;
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
                AssertEqual(expectedResult, result, disposeWaitHandle: true);
            }
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
                callbackCalled = true;
                callbackResult = ar;
                AssertEqual(expectedResult, ar);
            };

            using (EventWaitHandle asyncWaitHandle = new ManualResetEvent(initialState: false))
            {
                Mock<Stream> innerStreamMock = CreateMockInnerStream();
                AsyncCallback innerCallback = null;
                IAsyncResult innerResult = null;
                innerStreamMock
                    .Setup(s => s.BeginRead(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                        It.IsAny<AsyncCallback>(), It.IsAny<object>()))
                    .Returns<byte[], int, int, AsyncCallback, object>((i1, i2, i3, c, innerState) =>
                    {
                        innerCallback = c;
                        innerResult = CreateAsyncResult(innerState, asyncWaitHandle, completedSynchronously: false);
                        return innerResult;
                    });
                innerStreamMock
                    .Setup(s => s.EndRead(It.IsAny<IAsyncResult>()))
                    .Returns(-1);
                Stream innerStream = innerStreamMock.Object;
                product = CreateProductUnderTest(innerStream);

                byte[] buffer = new byte[0];
                int offset = 123;
                int count = 456;

                IAsyncResult result = product.BeginRead(buffer, offset, count, callback, expectedState);

                asyncWaitHandle.Set();

                // Act
                innerCallback.Invoke(innerResult);

                // Assert
                Assert.True(callbackCalled);
                // An AsyncCallback must be called with the same IAsyncResult instance as the Begin method returned.
                Assert.Same(result, callbackResult);
                AssertEqual(expectedResult, result, disposeWaitHandle: true);
            }
        }

        [Fact]
        public void EndRead_DelegatesToInnerStreamEndRead()
        {
            // Arrange
            IAsyncResult expectedResult = CreateStubAsyncResult();
            int expectedBytesRead = 789;

            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            AsyncCallback innerCallback = null;
            innerStreamMock
                .Setup(s => s.BeginRead(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<AsyncCallback>(), It.IsAny<object>()))
                .Callback<byte[], int, int, AsyncCallback, object>((i1, i2, i3, c, i4) => innerCallback = c)
                .Returns(expectedResult);
            innerStreamMock
                .Setup(s => s.EndRead(expectedResult))
                .Returns(expectedBytesRead)
                .Verifiable();
            Stream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            AsyncCallback callback = (_) => { };
            object state = new object();

            IAsyncResult result = product.BeginRead(buffer, offset, count, callback, state);

            if (innerCallback != null)
            {
                innerCallback.Invoke(expectedResult);
            }

            // Act
            int bytesRead = product.EndRead(result);

            // Assert
            Assert.Equal(expectedBytesRead, bytesRead);
            innerStreamMock.Verify();
        }

        [Fact]
        public void EndRead_WhenInnerStreamThrows_PropogatesException()
        {
            // Arrange
            Exception expectedException = new Exception();
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            IAsyncResult innerResult = CreateStubAsyncResult();
            AsyncCallback innerCallback = null;
            innerStreamMock
                .Setup(s => s.BeginRead(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<AsyncCallback>(),
                    It.IsAny<object>()))
                .Callback<byte[], int, int, AsyncCallback, object>((i1, i2, i3, c, i4) => innerCallback = c)
                .Returns(innerResult);
            innerStreamMock
                .Setup(s => s.EndRead(It.IsAny<IAsyncResult>()))
                .Throws(expectedException);
            Stream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            AsyncCallback callback = (_) => { };
            object state = new object();
            IAsyncResult result = product.BeginRead(buffer, offset, count, callback, state);

            if (innerCallback != null)
            {
                innerCallback.Invoke(innerResult);
            }

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

            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.BeginWrite(expectedBuffer, expectedOffset, expectedCount, It.IsAny<AsyncCallback>(),
                    It.IsAny<object>()))
                .Returns(CreateStubAsyncResult)
                .Verifiable();
            Stream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            AsyncCallback callback = (_) => { };
            object state = new object();

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
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.BeginWrite(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<AsyncCallback>(),
                    It.IsAny<object>()))
                .Throws(expectedException);
            Stream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            AsyncCallback callback = (_) => { };
            object state = new object();

            // Act & Assert
            Exception exception = Assert.Throws<Exception>(() => product.BeginWrite(buffer, offset, count, callback,
                state));
            Assert.Same(expectedException, exception);
        }

        [Fact]
        public void BeginWrite_WhenNotYetCompleted_ReturnsUncompletedResult()
        {
            // Arrange
            using (EventWaitHandle asyncWaitHandle = new ManualResetEvent(initialState: false))
            {
                Mock<Stream> innerStreamMock = CreateMockInnerStream();
                innerStreamMock
                    .Setup(s => s.BeginWrite(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                        It.IsAny<AsyncCallback>(), It.IsAny<object>()))
                    .Returns<byte[], int, int, AsyncCallback, object>((i1, i2, i3, innerCallback, innerState) =>
                        CreateAsyncResult(innerState, asyncWaitHandle, completedSynchronously: false));
                Stream innerStream = innerStreamMock.Object;
                Stream product = CreateProductUnderTest(innerStream);

                byte[] buffer = new byte[0];
                int offset = 123;
                int count = 456;
                AsyncCallback callback = (_) => { };
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
                AssertEqual(expectedResult, result, disposeWaitHandle: true);
            }
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
                callbackCalled = true;
                callbackResult = ar;
                AssertEqual(expectedResult, ar);
            };

            using (EventWaitHandle asyncWaitHandle = new ManualResetEvent(initialState: true))
            {
                Mock<Stream> innerStreamMock = CreateMockInnerStream();
                IAsyncResult innerResult = null;
                innerStreamMock
                    .Setup(s => s.BeginWrite(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                        It.IsAny<AsyncCallback>(), It.IsAny<object>()))
                    .Returns<byte[], int, int, AsyncCallback, object>((i1, i2, i3, innerCallback, innerState) =>
                    {
                        innerResult = CreateAsyncResult(innerState, asyncWaitHandle, completedSynchronously: true);
                        innerCallback(innerResult);
                        return innerResult;
                    });
                innerStreamMock.Setup(s => s.EndWrite(It.IsAny<IAsyncResult>()));
                Stream innerStream = innerStreamMock.Object;
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
                AssertEqual(expectedResult, result, disposeWaitHandle: true);
            }
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
                callbackCalled = true;
                callbackResult = ar;
                AssertEqual(expectedResult, ar);
            };

            using (EventWaitHandle asyncWaitHandle = new ManualResetEvent(initialState: false))
            {
                Mock<Stream> innerStreamMock = CreateMockInnerStream();
                AsyncCallback innerCallback = null;
                IAsyncResult innerResult = null;
                innerStreamMock
                    .Setup(s => s.BeginWrite(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                        It.IsAny<AsyncCallback>(), It.IsAny<object>()))
                    .Returns<byte[], int, int, AsyncCallback, object>((i1, i2, i3, c, innerState) =>
                    {
                        innerCallback = c;
                        innerResult = CreateAsyncResult(innerState, asyncWaitHandle, completedSynchronously: false);
                        return innerResult;
                    });
                innerStreamMock.Setup(s => s.EndWrite(It.IsAny<IAsyncResult>()));
                Stream innerStream = innerStreamMock.Object;
                product = CreateProductUnderTest(innerStream);

                byte[] buffer = new byte[0];
                int offset = 123;
                int count = 456;

                IAsyncResult result = product.BeginWrite(buffer, offset, count, callback, expectedState);

                asyncWaitHandle.Set();

                // Act
                innerCallback.Invoke(innerResult);

                // Assert
                Assert.True(callbackCalled);
                // An AsyncCallback must be called with the same IAsyncResult instance as the Begin method returned.
                Assert.Same(result, callbackResult);
                AssertEqual(expectedResult, result, disposeWaitHandle: true);
            }
        }

        [Fact]
        public void EndWrite_DelegatesToInnerStreamEndWrite()
        {
            // Arrange
            IAsyncResult expectedResult = CreateStubAsyncResult();

            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.BeginWrite(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<AsyncCallback>(), It.IsAny<object>()))
                .Returns(expectedResult);
            innerStreamMock
                .Setup(s => s.EndWrite(expectedResult))
                .Verifiable();
            Stream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            AsyncCallback callback = (_) => { };
            object state = new object();

            IAsyncResult result = product.BeginWrite(buffer, offset, count, callback, state);

            // Act
            product.EndWrite(result);

            // Assert
            innerStreamMock.Verify();
        }

        [Fact]
        public void EndWrite_WhenInnerStreamThrows_PropogatesException()
        {
            // Arrange
            Exception expectedException = new Exception();
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.BeginWrite(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<AsyncCallback>(),
                    It.IsAny<object>()))
                .Returns(CreateStubAsyncResult);
            innerStreamMock
                .Setup(s => s.EndWrite(It.IsAny<IAsyncResult>()))
                .Throws(expectedException);
            Stream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            AsyncCallback callback = (_) => { };
            object state = new object();
            IAsyncResult result = product.BeginWrite(buffer, offset, count, callback, state);

            // Act & Assert
            Exception exception = Assert.Throws<Exception>(() => product.EndWrite(result));
            Assert.Same(expectedException, exception);
        }

        [Fact]
        public void Close_DelegatesToInnerStreamClose()
        {
            // Arrange
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.Close())
                .Verifiable();
            Stream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            product.Close();

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

            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.CopyToAsync(expectedDestination, expectedBufferSize, expectedCancellationToken))
                .Returns(expectedTask)
                .Verifiable();
            Stream innerStream = innerStreamMock.Object;
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
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.Flush())
                .Verifiable();
            Stream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            product.Flush();

            // Assert
            innerStreamMock.Verify();
        }

        [Fact]
        public void FlushAsync_DelegatesToInnerStreamFlushAsync()
        {
            // Arrange
            CancellationToken expectedCancellationToken = new CancellationToken(canceled: true);
            Task expectedTask = new Task(() => { });

            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.FlushAsync(expectedCancellationToken))
                .Returns(expectedTask)
                .Verifiable();
            Stream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            Task task = product.FlushAsync(expectedCancellationToken);

            // Assert
            Assert.Same(task, expectedTask);
            innerStreamMock.Verify();
        }

        [Fact]
        public void Read_DelegatesToInnerStreamRead()
        {
            byte[] expectedBuffer = new byte[0];
            int expectedOffset = 123;
            int expectedCount = 456;
            int expectedBytesRead = 789;

            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.Read(expectedBuffer, expectedOffset, expectedCount))
                .Returns(expectedBytesRead)
                .Verifiable();
            Stream innerStream = innerStreamMock.Object;
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

            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.ReadAsync(expectedBuffer, expectedOffset, expectedCount, expectedCancellationToken))
                .Returns(Task.FromResult(-1))
                .Verifiable();
            Stream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            product.ReadAsync(expectedBuffer, expectedOffset, expectedCount, expectedCancellationToken);

            // Assert
            innerStreamMock.Verify();
        }

        [Fact]
        public void ReadAsync_WhenInnerStreamThrows_PropogatesException()
        {
            // Arrange
            Exception expectedException = new Exception();
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Throws(expectedException);
            Stream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            CancellationToken cancellationToken = CancellationToken.None;

            // Act & Assert
            Exception exception = Assert.Throws<Exception>(
                () => product.ReadAsync(buffer, offset, count, cancellationToken));
            Assert.Same(expectedException, exception);
        }

        [Fact]
        public void ReadAsync_WhenInnerStreamHasNotYetCompleted_ReturnsIncompleteTask()
        {
            // Arrange
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            TaskCompletionSource<int> taskSource = new TaskCompletionSource<int>();
            innerStreamMock
                .Setup(s => s.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(taskSource.Task);
            Stream innerStream = innerStreamMock.Object;
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
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            TaskCompletionSource<int> taskSource = new TaskCompletionSource<int>();
            taskSource.SetResult(expectedBytesRead);
            innerStreamMock
                .Setup(s => s.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(taskSource.Task);
            Stream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task<int> task = product.ReadAsync(buffer, offset, count, cancellationToken);

            // Assert
            Assert.NotNull(task);
            task.WaitUntilCompleted(1000);
            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
            Assert.Equal(expectedBytesRead, task.Result);
        }

        [Fact]
        public void ReadAsync_WhenInnerStreamHasCanceled_ReturnsCanceledTask()
        {
            // Arrange
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            TaskCompletionSource<int> taskSource = new TaskCompletionSource<int>();
            taskSource.SetCanceled();
            innerStreamMock
                .Setup(s => s.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(taskSource.Task);
            Stream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task<int> task = product.ReadAsync(buffer, offset, count, cancellationToken);

            // Assert
            Assert.NotNull(task);
            task.WaitUntilCompleted(1000);
            Assert.Equal(TaskStatus.Canceled, task.Status);
        }

        [Fact]
        public void ReadAsync_WhenInnerStreamHasFaulted_ReturnsFaultedTask()
        {
            // Arrange
            Exception expectedException = new Exception();
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            TaskCompletionSource<int> taskSource = new TaskCompletionSource<int>();
            taskSource.SetException(expectedException);
            innerStreamMock
                .Setup(s => s.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(taskSource.Task);
            Stream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task<int> task = product.ReadAsync(buffer, offset, count, cancellationToken);

            // Assert
            Assert.NotNull(task);
            task.WaitUntilCompleted(1000);
            Assert.Equal(TaskStatus.Faulted, task.Status);
            Assert.NotNull(task.Exception);
            Assert.Same(expectedException, task.Exception.InnerException);
        }

        [Fact]
        public void ReadByte_DelegatesToInnerStreamReadByte()
        {
            // Arrange
            int expected = 123;
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock.Setup(s => s.ReadByte()).Returns(expected);
            Stream innerStream = innerStreamMock.Object;
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

            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.Seek(expectedOffset, expectedOrigin))
                .Returns(expectedPosition)
                .Verifiable();
            Stream innerStream = innerStreamMock.Object;
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

            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.SetLength(expectedValue))
                .Verifiable();
            Stream innerStream = innerStreamMock.Object;
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

            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.Write(expectedBuffer, expectedOffset, expectedCount))
                .Verifiable();
            Stream innerStream = innerStreamMock.Object;
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

            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.WriteAsync(expectedBuffer, expectedOffset, expectedCount, expectedCancellationToken))
                .Returns(Task.FromResult(-1))
                .Verifiable();
            Stream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            product.WriteAsync(expectedBuffer, expectedOffset, expectedCount, expectedCancellationToken);

            // Assert
            innerStreamMock.Verify();
        }

        [Fact]
        public void WriteAsync_WhenInnerStreamThrows_PropogatesException()
        {
            // Arrange
            Exception expectedException = new Exception();
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.WriteAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Throws(expectedException);
            Stream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            CancellationToken cancellationToken = CancellationToken.None;

            // Act & Assert
            Exception exception = Assert.Throws<Exception>(
                () => product.WriteAsync(buffer, offset, count, cancellationToken));
            Assert.Same(expectedException, exception);
        }

        [Fact]
        public void WriteAsync_WhenInnerStreamHasNotYetCompleted_ReturnsIncompleteTask()
        {
            // Arrange
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            TaskCompletionSource<object> taskSource = new TaskCompletionSource<object>();
            innerStreamMock
                .Setup(s => s.WriteAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(taskSource.Task);
            Stream innerStream = innerStreamMock.Object;
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
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            TaskCompletionSource<object> taskSource = new TaskCompletionSource<object>();
            taskSource.SetResult(null);
            innerStreamMock
                .Setup(s => s.WriteAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(taskSource.Task);
            Stream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task task = product.WriteAsync(buffer, offset, count, cancellationToken);

            // Assert
            Assert.NotNull(task);
            task.WaitUntilCompleted(1000);
            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
        }

        [Fact]
        public void WriteAsync_WhenInnerStreamHasCanceled_ReturnsCanceledTask()
        {
            // Arrange
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            TaskCompletionSource<int> taskSource = new TaskCompletionSource<int>();
            taskSource.SetCanceled();
            innerStreamMock
                .Setup(s => s.WriteAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(taskSource.Task);
            Stream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task task = product.WriteAsync(buffer, offset, count, cancellationToken);

            // Assert
            Assert.NotNull(task);
            task.WaitUntilCompleted(1000);
            Assert.Equal(TaskStatus.Canceled, task.Status);
        }

        [Fact]
        public void WriteAsync_WhenInnerStreamHasFaulted_ReturnsFaultedTask()
        {
            // Arrange
            Exception expectedException = new Exception();
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            TaskCompletionSource<int> taskSource = new TaskCompletionSource<int>();
            taskSource.SetException(expectedException);
            innerStreamMock
                .Setup(s => s.WriteAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(taskSource.Task);
            Stream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            byte[] buffer = new byte[0];
            int offset = 123;
            int count = 456;
            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task task = product.WriteAsync(buffer, offset, count, cancellationToken);

            // Assert
            Assert.NotNull(task);
            task.WaitUntilCompleted(1000);
            Assert.Equal(TaskStatus.Faulted, task.Status);
            Assert.NotNull(task.Exception);
            Assert.Same(expectedException, task.Exception.InnerException);
        }

        [Fact]
        public void WriteByte_DelegatesToInnerStreamWriteByte()
        {
            // Arrange
            byte expected = 123;
            Mock<Stream> innerStreamMock = CreateMockInnerStream();
            innerStreamMock
                .Setup(s => s.WriteByte(expected))
                .Verifiable();
            Stream innerStream = innerStreamMock.Object;
            Stream product = CreateProductUnderTest(innerStream);

            // Act
            product.WriteByte(expected);

            // Assert
            innerStreamMock.Verify();
        }

        [Fact]
        public void GetStatus_Initially_ReturnsZeroBytesRead()
        {
            // Arrange
            using (MemoryStream innerStream = CreateInnerStream(String.Empty))
            using (WatchableReadStream product = CreateProductUnderTest(innerStream))
            {
                // Act
                ParameterLog status = product.GetStatus();

                // Assert
                AssertEqualBytesRead(0, status);
            }
        }

        [Fact]
        public void GetStatus_AfterRead_ReturnsBytesRead()
        {
            // Arrange
            string contents = "abc";

            using (MemoryStream innerStream = CreateInnerStream(contents))
            using (WatchableReadStream product = CreateProductUnderTest(innerStream))
            {
                byte[] buffer = new byte[contents.Length];
                int bytesRead = product.Read(buffer, 0, buffer.Length);
                Assert.Equal(bytesRead, buffer.Length); // Guard

                // Act
                ParameterLog status = product.GetStatus();

                // Assert
                AssertEqualBytesRead(contents.Length, status);
            }
        }

        [Fact]
        public void GetStatus_AfterReadByte_ReturnsOneByteRead()
        {
            // Arrange
            string contents = "abc";

            using (MemoryStream innerStream = CreateInnerStream(contents))
            using (WatchableReadStream product = CreateProductUnderTest(innerStream))
            {
                int read = product.ReadByte();
                Assert.NotEqual(-1, read); // Guard

                // Act
                ParameterLog status = product.GetStatus();

                // Assert
                AssertEqualBytesRead(1, status);
            }
        }

        [Fact]
        public void GetStatus_AfterReadByteTwice_ReturnsTwoBytesRead()
        {
            // Arrange
            string contents = "abc";

            using (MemoryStream innerStream = CreateInnerStream(contents))
            using (WatchableReadStream product = CreateProductUnderTest(innerStream))
            {
                int read = product.ReadByte();
                Assert.NotEqual(-1, read); // Guard
                read = product.ReadByte();
                Assert.NotEqual(-1, read); // Guard

                // Act
                ParameterLog status = product.GetStatus();

                // Assert
                AssertEqualBytesRead(2, status);
            }
        }

        [Fact]
        public void GetStatus_AfterReadByteNegativeOne_ReturnsZeroBytesRead()
        {
            // Arrange
            string contents = String.Empty;

            using (MemoryStream innerStream = CreateInnerStream(contents))
            using (WatchableReadStream product = CreateProductUnderTest(innerStream))
            {
                int read = product.ReadByte();
                Assert.Equal(-1, read); // Guard

                // Act
                ParameterLog status = product.GetStatus();

                // Assert
                AssertEqualBytesRead(0, status);
            }
        }

        [Fact]
        public void GetStatus_AfterReadAsync_ReturnsBytesRead()
        {
            // Arrange
            string contents = "abc";

            using (MemoryStream innerStream = CreateInnerStream(contents))
            using (WatchableReadStream product = CreateProductUnderTest(innerStream))
            {
                byte[] buffer = new byte[contents.Length];
                int bytesRead = product.ReadAsync(buffer, 0, buffer.Length).Result;
                Assert.Equal(bytesRead, buffer.Length); // Guard

                // Act
                ParameterLog status = product.GetStatus();

                // Assert
                AssertEqualBytesRead(contents.Length, status);
            }
        }

        [Fact]
        public void GetStatus_AfterBeginEndRead_ReturnsBytesRead()
        {
            // Arrange
            string contents = "abc";

            using (MemoryStream innerStream = CreateInnerStream(contents))
            using (WatchableReadStream product = CreateProductUnderTest(innerStream))
            {
                byte[] buffer = new byte[contents.Length];
                int bytesRead = product.EndRead(product.BeginRead(buffer, 0, buffer.Length, null, null));
                Assert.Equal(bytesRead, buffer.Length); // Guard

                // Act
                ParameterLog status = product.GetStatus();

                // Assert
                AssertEqualBytesRead(contents.Length, status);
            }
        }

        private static void AssertEqual(ExpectedAsyncResult expected, IAsyncResult actual,
            bool disposeWaitHandle = false)
        {
            Assert.NotNull(actual);
            Assert.Same(expected.AsyncState, actual.AsyncState);
            Assert.Equal(expected.CompletedSynchronously, actual.CompletedSynchronously);
            Assert.Equal(expected.IsCompleted, actual.IsCompleted);

            IDisposable disposable = disposeWaitHandle ? actual.AsyncWaitHandle : null;

            using (disposable)
            {
                Assert.Equal(expected.IsCompleted, actual.AsyncWaitHandle.WaitOne(0));
            }
        }

        private static void AssertEqualBytesRead(int expected, ParameterLog actual)
        {
            Assert.IsType<ReadBlobParameterLog>(actual);
            ReadBlobParameterLog actualBlobLog = (ReadBlobParameterLog)actual;
            Assert.Equal(expected, actualBlobLog.BytesRead);
        }

        private static IAsyncResult CreateAsyncResult(object asyncState, EventWaitHandle asyncWaitHandle,
            bool completedSynchronously)
        {
            return new FakeAsyncResult(asyncState, asyncWaitHandle, completedSynchronously);
        }

        private static MemoryStream CreateInnerStream(string contents)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(contents), writable: false);
        }

        private static Stream CreateDummyStream()
        {
            return new Mock<Stream>(MockBehavior.Strict).Object;
        }

        private static Mock<Stream> CreateMockInnerStream()
        {
            Mock<Stream> mock = new Mock<Stream>(MockBehavior.Strict);
            mock.Setup(s => s.Length).Returns(0); // Used by WatchableReadStream constructor
            return mock;
        }

        private static WatchableReadStream CreateProductUnderTest(Stream inner)
        {
            return new WatchableReadStream(inner);
        }

        private static IAsyncResult CreateStubAsyncResult()
        {
            return new Mock<IAsyncResult>().Object;
        }

        private struct ExpectedAsyncResult
        {
            public object AsyncState;
            public bool CompletedSynchronously;
            public bool IsCompleted;
        }

        private class FakeAsyncResult : IAsyncResult
        {
            private readonly object _asyncState;
            private readonly EventWaitHandle _asyncWaitHandle;
            private readonly bool _completedSynchronously;

            public FakeAsyncResult(object asyncState, EventWaitHandle asyncWaitHandle, bool completedSynchronously)
            {
                _asyncState = asyncState;
                _asyncWaitHandle = asyncWaitHandle;
                _completedSynchronously = completedSynchronously;
            }

            public object AsyncState
            {
                get { return _asyncState; }
            }

            public WaitHandle AsyncWaitHandle
            {
                get { return _asyncWaitHandle; }
            }

            public bool CompletedSynchronously
            {
                get { return _completedSynchronously; }
            }

            public bool IsCompleted
            {
                get { return _asyncWaitHandle.WaitOne(0); }
            }
        }
    }
}
