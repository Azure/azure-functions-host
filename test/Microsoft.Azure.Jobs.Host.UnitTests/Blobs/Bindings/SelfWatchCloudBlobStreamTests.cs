using System;
using System.IO;
using Microsoft.Azure.Jobs.Host.Blobs.Bindings;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Blobs.Bindings
{
    public class SelfWatchCloudBlobStreamTests
    {
        [Fact]
        public void Complete_NotYetClosedAndNothingWasWritten_ReturnFalse()
        {
            // Arrange
            using (var memoryStream = new MemoryCloudBlobStream())
            {
                var watchableStream = new SelfWatchCloudBlobStream(memoryStream, null);

                // Act
                var result = watchableStream.Complete();

                // Assert
                Assert.False(result);
            }
        }

        [Fact]
        public void Complete_ClosedAndNothingWasWritten_ReturnTrue()
        {
            // Arrange
            using (var memoryStream = new MemoryCloudBlobStream())
            {
                var watchableStream = new SelfWatchCloudBlobStream(memoryStream, NullBlobCommittedAction.Instance);
                watchableStream.Close();

                // Act
                var result = watchableStream.Complete();

                // Assert
                Assert.True(result);
            }
        }

        [Fact]
        public void Complete_NotClosedButWrittenTo_ReturnTrue()
        {
            // Arrange
            using (var memoryStream = new MemoryCloudBlobStream())
            {
                var watchableStream = new SelfWatchCloudBlobStream(memoryStream, NullBlobCommittedAction.Instance);
                watchableStream.WriteByte(1);

                // Act
                var result = watchableStream.Complete();

                // Assert
                Assert.True(result);
            }
        }

        [Fact]
        public void Complete_WrittenToAndClosed_ReturnTrue()
        {
            // Arrange
            using (var memoryStream = new MemoryCloudBlobStream())
            {
                var watchableStream = new SelfWatchCloudBlobStream(memoryStream, NullBlobCommittedAction.Instance);
                watchableStream.WriteByte(1);
                watchableStream.Close();

                // Act
                var result = watchableStream.Complete();

                // Assert
                Assert.True(result);
            }
        }

        [Fact]
        public void Complete_AlreadyComplete_DoesNotReset()
        {
            // Arrange
            using (var s = new MemoryCloudBlobStream())
            {
                var w = new SelfWatchCloudBlobStream(s, null);
                w.Complete();

                // Act
                var result = w.Complete();

                // Assert
                // Should still be false, even though the stream was closed on the call,
                // as the calculation only happen on first call to .Complete()
                Assert.False(result); 
            }
        }

        [Fact]
        public void GetStatus_NothingWasWrittenThenClosed_WroteZeroBytes()
        {
            using (var s = new MemoryCloudBlobStream())
            {
                var w = new SelfWatchCloudBlobStream(s, NullBlobCommittedAction.Instance);
                w.Close();
                w.Complete();

                // Act
                var result = w.GetStatus();

                // Assert
                Assert.Equal("Wrote 0 bytes.", result);
            }
        }

        [Fact]
        public void GetStatus_NothingWasWrittenThenNotClosed_NothingWasWritten()
        {
            using (var s = new MemoryCloudBlobStream())
            {
                var w = new SelfWatchCloudBlobStream(s, NullBlobCommittedAction.Instance);
                w.Complete();

                // Act
                var result = w.GetStatus();

                // Assert
                Assert.Equal("Nothing was written.", result);
            }
        }

        [Fact]
        public void GetStatus_ClosedButNotComplete_WroteZeroBytes()
        {
            // Arrange
            using (var s = new MemoryCloudBlobStream())
            {
                var w = new SelfWatchCloudBlobStream(s, null);
                w.Close();

                // Act
                var result = w.GetStatus();

                // Assert
                Assert.Equal("Wrote 0 bytes.", result);
            }
        }

        [Fact]
        public void GetStatus_NotClosedAndNotComplete_EmptyStatus()
        {
            // Arrange
            using (var s = new MemoryCloudBlobStream())
            {
                var w = new SelfWatchCloudBlobStream(s, null);

                // Act
                var result = w.GetStatus();

                // Assert
                Assert.Equal(String.Empty, result);
            }
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

            public void Execute()
            {
            }
        }

        private class MemoryCloudBlobStream : CloudBlobStream
        {
            private readonly MemoryStream _inner = new MemoryStream();

            public override ICancellableAsyncResult BeginCommit(AsyncCallback callback, object state)
            {
                throw new NotImplementedException();
            }

            public override ICancellableAsyncResult BeginFlush(AsyncCallback callback, object state)
            {
                throw new NotImplementedException();
            }

            public override void Commit()
            {
            }

            public override void EndCommit(IAsyncResult asyncResult)
            {
                throw new NotImplementedException();
            }

            public override void EndFlush(IAsyncResult asyncResult)
            {
                throw new NotImplementedException();
            }

            public override bool CanRead
            {
                get { return _inner.CanRead; }
            }

            public override bool CanSeek
            {
                get { return _inner.CanSeek; }
            }

            public override bool CanWrite
            {
                get { return _inner.CanWrite; }
            }

            public override void Flush()
            {
                _inner.Flush();
            }

            public override long Length
            {
                get { return _inner.Length; }
            }

            public override long Position
            {
                get
                {
                    return _inner.Position;
                }
                set
                {
                    _inner.Position = value;
                }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return _inner.Read(buffer, offset, count);
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                return _inner.Seek(offset, origin);
            }

            public override void SetLength(long value)
            {
                _inner.SetLength(value);
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                _inner.Write(buffer, offset, count);
            }

            public override void Close()
            {
                _inner.Close();
            }
        }
    }
}
