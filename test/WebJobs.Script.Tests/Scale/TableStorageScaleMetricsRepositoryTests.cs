// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using Azure;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Scale
{
    public class TableStorageScaleMetricsRepositoryTests
    {
        [Theory]
        [InlineData((int)HttpStatusCode.TooManyRequests, true)]  // 429
        [InlineData((int)HttpStatusCode.InternalServerError, true)]  // 500
        [InlineData((int)HttpStatusCode.ServiceUnavailable, true)]  // 503
        [InlineData((int)HttpStatusCode.GatewayTimeout, true)]  // 504
        [InlineData((int)HttpStatusCode.NotFound, false)]  // 404 - not transient
        [InlineData((int)HttpStatusCode.BadRequest, false)]  // 400 - not transient
        [InlineData((int)HttpStatusCode.Unauthorized, false)]  // 401 - not transient
        [InlineData((int)HttpStatusCode.Forbidden, false)]  // 403 - not transient
        [InlineData((int)HttpStatusCode.Conflict, false)]  // 409 - not transient
        [InlineData((int)HttpStatusCode.RequestTimeout, false)]  // 408 - not in transient list
        public void IsTransientStorageError_ReturnsExpectedResult(int statusCode, bool expectedResult)
        {
            // Arrange
            var exception = new RequestFailedException(statusCode, "Test error message", "TestErrorCode", null);

            // Act
            var result = TableStorageScaleMetricsRepository.IsTransientStorageError(exception);

            // Assert
            Assert.Equal(expectedResult, result);
        }

        [Fact]
        public void IsTransientStorageError_WithNullException_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => TableStorageScaleMetricsRepository.IsTransientStorageError(null));
        }
    }
}
