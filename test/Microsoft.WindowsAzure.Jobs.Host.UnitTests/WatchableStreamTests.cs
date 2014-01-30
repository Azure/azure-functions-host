using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs.Host.TestCommon;

namespace Microsoft.WindowsAzure.Jobs.Host.UnitTests
{
    [TestClass]
    public class WatchableStreamTests
    {
        [TestMethod]
        public void Complete_NotYetClosedAndNothingWasWritten_ReturnFalse()
        {
            // Arrange
            using (var memoryStream = new MemoryStream())
            {
                var watchableStream = new WatchableStream(memoryStream);

                // Act
                var result = watchableStream.Complete();

                // Assert
                Assert.IsFalse(result);
            }
        }

        [TestMethod]
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
                Assert.IsTrue(result);
            }
        }

        [TestMethod]
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
                Assert.IsTrue(result);
            }
        }

        [TestMethod]
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
                Assert.IsTrue(result);
            }
        }

        [TestMethod]
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
                Assert.IsFalse(result); 
            }
        }

        [TestMethod]
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
                Assert.AreEqual("Wrote 0 bytes.", result);
            }
        }

        [TestMethod]
        public void GetStatus_NothingWasWrittenThenNotClosed_NothingWasWritten()
        {
            using (var s = new MemoryStream())
            {
                var w = new WatchableStream(s);
                w.Complete();

                // Act
                var result = w.GetStatus();

                // Assert
                Assert.AreEqual("Nothing was written.", result);
            }
        }

        [TestMethod]
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
                Assert.AreEqual("Nothing was written.", result);
            }
        }

        [TestMethod]
        public void GetStatus_NotClosedAndNotComplete_EmptyStatus()
        {
            // Arrange
            using (var s = new MemoryStream())
            {
                var w = new WatchableStream(s);

                // Act
                var result = w.GetStatus();

                // Assert
                Assert.AreEqual(string.Empty, result);
            }
        }
    }
}
