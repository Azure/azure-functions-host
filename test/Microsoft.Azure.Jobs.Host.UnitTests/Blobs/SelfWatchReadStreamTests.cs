using System;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Azure.Jobs.Host.Blobs;
using Xunit;
using Xunit.Extensions;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Blobs
{
    public class SelfWatchReadStreamTests
    {
        [Fact]
        public void AppendNetworkTime_ZeroTime_NoOutput()
        {
            // Arrange
            var sb = new StringBuilder();
            var time = TimeSpan.Zero;

            // Act
            SelfWatchReadStream.AppendNetworkTime(sb, time);
            var result = sb.ToString();

            // Assert
            Assert.Equal(string.Empty, result);
        }

        [Theory]
        [InlineData(0.1, "1 millisecond")]
        [InlineData(0.9, "1 millisecond")]
        [InlineData(1, "1 millisecond")]
        [InlineData(1.5, "2 milliseconds")]
        [InlineData(1.7, "2 milliseconds")]
        [InlineData(2.1, "2 milliseconds")]
        [InlineData(900, "900 milliseconds")]
        [InlineData(951, "1 second")]
        public void AppendNetworkTime_Milliseconds(double milliseconds, string expected)
        {
            // Arrange
            var sb = new StringBuilder();
            // this is required since the FromMilliseconds method rounds the 
            // result (so for e.g. FromMilliseconds(0.1) will create Timespan.Zero
            // which is not what we are trying to test here).
            var time = TimeSpan.FromTicks((long) (milliseconds*10000));

            // Act
            SelfWatchReadStream.AppendNetworkTime(sb, time);
            var result = sb.ToString();

            // Assert
            AssertExpectedNetworkTimeString(expected, result);
        }

        [Theory]
        [InlineData(1, "1 second")]
        [InlineData(1.5, "2 seconds")]
        [InlineData(1.7, "2 seconds")]
        [InlineData(2.2, "2 seconds")]
        [InlineData(55, "55 seconds")]
        [InlineData(56, "1 minute")]
        public void AppendNetworkTime_Seconds(double seconds, string expected)
        {
            // Arrange
            var sb = new StringBuilder();
            var time = TimeSpan.FromSeconds(seconds);
            
            // Act
            SelfWatchReadStream.AppendNetworkTime(sb, time);
            var result = sb.ToString();

            // Assert
            AssertExpectedNetworkTimeString(expected, result);
        }

        [Theory]
        [InlineData(1, "1 minute")]
        [InlineData(1.5, "2 minutes")]
        [InlineData(1.7, "2 minutes")]
        [InlineData(2.1, "2 minutes")]
        [InlineData(55, "55 minutes")]
        [InlineData(56, "1 hour")]
        public void AppendNetworkTime_Minutes(double minutes, string expected)
        {
            // Arrange
            var sb = new StringBuilder();
            var time = TimeSpan.FromMinutes(minutes);

            // Act
            SelfWatchReadStream.AppendNetworkTime(sb, time);
            var result = sb.ToString();

            // Assert
            AssertExpectedNetworkTimeString(expected, result);
        }

        [Theory]
        [InlineData(1, "1 hour")]
        [InlineData(1.5, "2 hours")]
        [InlineData(1.7, "2 hours")]
        [InlineData(2.1, "2 hours")]
        [InlineData(25, "25 hours")]
        public void AppendNetworkTime_Hours(double hours, string expected)
        {
            // Arrange
            var sb = new StringBuilder();
            var time = TimeSpan.FromHours(hours);

            // Act
            SelfWatchReadStream.AppendNetworkTime(sb, time);
            var result = sb.ToString();

            // Assert
            AssertExpectedNetworkTimeString(expected, result);
        }

        private static void AssertExpectedNetworkTimeString(string expected, string actual)
        {
            var expectedFullString = String.Format(CultureInfo.CurrentCulture, "(about {0} spent on I/O)", expected);
            Assert.Equal(expectedFullString, actual);
        }
    }
}
