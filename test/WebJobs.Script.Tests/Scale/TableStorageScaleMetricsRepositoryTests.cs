// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net;
using Azure;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Scale
{
    public class TableStorageScaleMetricsRepositoryTests
    {
        [Theory]
        [InlineData(HttpStatusCode.TooManyRequests, true)]
        [InlineData(HttpStatusCode.InternalServerError, true)]
        [InlineData(HttpStatusCode.ServiceUnavailable, true)]
        [InlineData(HttpStatusCode.GatewayTimeout, true)]
        [InlineData(HttpStatusCode.NotFound, false)]
        [InlineData(HttpStatusCode.BadRequest, false)]
        [InlineData(HttpStatusCode.Forbidden, false)]
        [InlineData(HttpStatusCode.Unauthorized, false)]
        [InlineData(HttpStatusCode.Conflict, false)]
        [InlineData(HttpStatusCode.OK, false)]
        public void IsTransientStorageError_ReturnsExpected(HttpStatusCode statusCode, bool expected)
        {
            var exception = new RequestFailedException((int)statusCode, "Test error message");

            var result = TableStorageScaleMetricsRepository.IsTransientStorageError(exception);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void IsTransientStorageError_ThrowsArgumentNullException_WhenExceptionIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => TableStorageScaleMetricsRepository.IsTransientStorageError(null));
        }
    }
}
