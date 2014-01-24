using System;
using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs.Host.TestCommon;

namespace Microsoft.WindowsAzure.Jobs.Host.UnitTests
{
    [TestClass]
    public class SelfWatchTests
    {
        [TestMethod]
        public void EncodeSelfWatchStatus_StringWithNewLines_ReplacedWithDelimiters()
        {
            // Arrange
            var input = "Some" + Environment.NewLine + "status";
            var expected = "Some; status";

            // Act
            var output = SelfWatch.EncodeSelfWatchStatus(input);

            //Assert
            Assert.AreEqual(expected, output);
        }

        [TestMethod]
        public void EncodeSelfWatchStatus_NullInput_Throws()
        {
            // Arrange
            string input = null;

            // Act and assert
            ExceptionAssert.ThrowsArgumentNull(() => SelfWatch.EncodeSelfWatchStatus(input), "status");
        }

        [TestMethod]
        public void DecodeSelfWatchStatus_StringWithDelimiters_ReplacedWithNewLines()
        {
            // Arrange
            var input = "Some; status";
            var expected = "Some" + Environment.NewLine + "status";

            // Act
            var output = SelfWatch.DecodeSelfWatchStatus(input);

            //Assert
            Assert.AreEqual(expected, output);
        }

        [TestMethod]
        public void DecodeSelfWatchStatus_NullInput_Throws()
        {
            // Arrange
            string input = null;

            // Act and assert
            ExceptionAssert.ThrowsArgumentNull(() => SelfWatch.DecodeSelfWatchStatus(input), "status");
        }
    }
}