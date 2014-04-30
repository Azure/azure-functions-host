using System;
using Microsoft.Azure.Jobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests
{
    public class SelfWatchTests
    {
        [Fact]
        public void EncodeSelfWatchStatus_StringWithNewLines_ReplacedWithDelimiters()
        {
            // Arrange
            var input = "Some" + Environment.NewLine + "status";
            var expected = "Some; status";

            // Act
            var output = SelfWatch.EncodeSelfWatchStatus(input);

            //Assert
            Assert.Equal(expected, output);
        }

        [Fact]
        public void EncodeSelfWatchStatus_NullInput_Throws()
        {
            // Arrange
            string input = null;

            // Act and assert
            ExceptionAssert.ThrowsArgumentNull(() => SelfWatch.EncodeSelfWatchStatus(input), "status");
        }

        [Fact]
        public void DecodeSelfWatchStatus_StringWithDelimiters_ReplacedWithNewLines()
        {
            // Arrange
            var input = "Some; status";
            var expected = "Some" + Environment.NewLine + "status";

            // Act
            var output = SelfWatch.DecodeSelfWatchStatus(input);

            //Assert
            Assert.Equal(expected, output);
        }

        [Fact]
        public void DecodeSelfWatchStatus_NullInput_Throws()
        {
            // Arrange
            string input = null;

            // Act and assert
            ExceptionAssert.ThrowsArgumentNull(() => SelfWatch.DecodeSelfWatchStatus(input), "status");
        }
    }
}