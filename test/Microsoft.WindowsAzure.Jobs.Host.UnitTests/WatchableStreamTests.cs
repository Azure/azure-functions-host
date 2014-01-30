using System.IO;
using Xunit;

namespace Microsoft.WindowsAzure.Jobs.Host.UnitTests
{
    public class WatchableStreamTests
    {
        [Fact]
        public void Complete_NotYetClosedAndNothingWasWritten_ReturnFalse()
        {
            // Arrange
            using (var memoryStream = new MemoryStream())
            {
                var watchableStream = new WatchableStream(memoryStream);

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
            using (var memoryStream = new MemoryStream())
            {
                var watchableStream = new WatchableStream(memoryStream);
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
            using (var memoryStream = new MemoryStream())
            {
                var watchableStream = new WatchableStream(memoryStream);
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
            using (var memoryStream = new MemoryStream())
            {
                var watchableStream = new WatchableStream(memoryStream);
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
            using (var s = new MemoryStream())
            {
                var w = new WatchableStream(s);
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
            using (var s = new MemoryStream())
            {
                var w = new WatchableStream(s);
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
            using (var s = new MemoryStream())
            {
                var w = new WatchableStream(s);
                w.Complete();

                // Act
                var result = w.GetStatus();

                // Assert
                Assert.Equal("Nothing was written.", result);
            }
        }

        [Fact]
        public void GetStatus_ClosedButNotComplete_NothingWasWritten()
        {
            // Arrange
            using (var s = new MemoryStream())
            {
                var w = new WatchableStream(s);
                w.Close();

                // Act
                var result = w.GetStatus();

                // Assert
                Assert.Equal("Nothing was written.", result);
            }
        }

        [Fact]
        public void GetStatus_NotClosedAndNotComplete_EmptyStatus()
        {
            // Arrange
            using (var s = new MemoryStream())
            {
                var w = new WatchableStream(s);

                // Act
                var result = w.GetStatus();

                // Assert
                Assert.Equal(string.Empty, result);
            }
        }
    }
}
