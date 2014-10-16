// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Text;
using Microsoft.Azure.WebJobs.Protocols;
using Xunit;
using Xunit.Extensions;

namespace Dashboard.UnitTests.Infrastructure
{
    public class LogAnalysisTests
    {
        [Fact]
        public void FormatTime_ZeroTime_NoOutput()
        {
            // Arrange
            var time = TimeSpan.Zero;

            // Act
            string result = LogAnalysis.Format(time);

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
        public void FormatTime_Milliseconds(double milliseconds, string expected)
        {
            // Arrange
            // this is required since the FromMilliseconds method rounds the 
            // result (so for e.g. FromMilliseconds(0.1) will create Timespan.Zero
            // which is not what we are trying to test here).
            var time = TimeSpan.FromTicks((long) (milliseconds*10000));

            // Act
            string result = LogAnalysis.Format(time);

            // Assert
            AssertExpectedTimeString(expected, result);
        }

        [Theory]
        [InlineData(1, "1 second")]
        [InlineData(1.5, "2 seconds")]
        [InlineData(1.7, "2 seconds")]
        [InlineData(2.2, "2 seconds")]
        [InlineData(55, "55 seconds")]
        [InlineData(56, "1 minute")]
        public void FormatTime_Seconds(double seconds, string expected)
        {
            // Arrange
            var time = TimeSpan.FromSeconds(seconds);
            
            // Act
            string result = LogAnalysis.Format(time);

            // Assert
            AssertExpectedTimeString(expected, result);
        }

        [Theory]
        [InlineData(1, "1 minute")]
        [InlineData(1.5, "2 minutes")]
        [InlineData(1.7, "2 minutes")]
        [InlineData(2.1, "2 minutes")]
        [InlineData(55, "55 minutes")]
        [InlineData(56, "1 hour")]
        public void FormatTime_Minutes(double minutes, string expected)
        {
            // Arrange
            var time = TimeSpan.FromMinutes(minutes);

            // Act
            string result = LogAnalysis.Format(time);

            // Assert
            AssertExpectedTimeString(expected, result);
        }

        [Theory]
        [InlineData(1, "1 hour")]
        [InlineData(1.5, "2 hours")]
        [InlineData(1.7, "2 hours")]
        [InlineData(2.1, "2 hours")]
        [InlineData(25, "25 hours")]
        public void FormatTime_Hours(double hours, string expected)
        {
            // Arrange
            var time = TimeSpan.FromHours(hours);

            // Act
            string result = LogAnalysis.Format(time);

            // Assert
            AssertExpectedTimeString(expected, result);
        }

        private static void AssertExpectedTimeString(string expected, string actual)
        {
            var expectedFullString = String.Format(CultureInfo.CurrentCulture, "About {0}", expected);
            Assert.Equal(expectedFullString, actual);
        }

        [Theory]
        [InlineData(27, 27, 27.7, "Read 27 bytes (100.00% of total). About 28 milliseconds spent on I/O.")]
        [InlineData(27, 27, .0, "Read 27 bytes (100.00% of total).")]
        public void Format_ReadBlobParameterLog(long bytesRead, long logLength, double elapsedTime, string expected)
        {
            ParameterLog log = new ReadBlobParameterLog()
            {
                BytesRead = bytesRead,
                Length = logLength,
                ElapsedTime = TimeSpan.FromMilliseconds(elapsedTime)
            };

            string result = LogAnalysis.Format(log);

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(true, 27, "Wrote 27 bytes.")]
        [InlineData(false, 27, "Nothing was written.")]
        public void Format_WriteBlobParameterLog(bool wasWritten, long bytesWritten, string expected)
        {
            ParameterLog log = new WriteBlobParameterLog()
            {
                WasWritten = wasWritten,
                BytesWritten = bytesWritten
            };

            string result = LogAnalysis.Format(log);

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(1, 27.7, "Wrote 1 entity. About 28 milliseconds spent on I/O.")]
        [InlineData(27, 27.7, "Wrote 27 entities. About 28 milliseconds spent on I/O.")]
        [InlineData(27, .0, "Wrote 27 entities.")]
        public void Format_TableParameterLog(int entitiesWritten, double elapsedTime, string expected)
        {
            ParameterLog log = new TableParameterLog()
            {
                EntitiesWritten = entitiesWritten,
                ElapsedWriteTime = TimeSpan.FromMilliseconds(elapsedTime)
            };

            string result = LogAnalysis.Format(log);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void Format_TextParameterLog()
        {
            const string expected = "Pass-Through";
            ParameterLog log = new TextParameterLog()
            {
                Value = expected
            };

            string result = LogAnalysis.Format(log);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void Format_BinderParameterLog_ReturnsNull()
        {
            ParameterLog log = new BinderParameterLog();

            string result = LogAnalysis.Format(log);

            Assert.Null(result);
        }
    }
}
