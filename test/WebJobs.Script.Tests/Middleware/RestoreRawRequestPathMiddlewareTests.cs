// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Moq;
using Xunit;

public class RestoreRawRequestPathMiddlewareTests
{
    [Fact]
    public async Task Invoke_WithUnencodedHeaderValue_SetsRequestPath()
    {
        // Arrange
        const string unEncodedHeaderValue = "/cloud%2Fdevdiv";
        const string requestPathValue = "/cloud/devdiv";
        var context = CreateHttpContextWithHeader(requestPathValue, unEncodedHeaderValue);
        var mockNext = new Mock<RequestDelegate>();
        var middleware = new RestoreRawRequestPathMiddleware(mockNext.Object);
        var originalPath = context.Request.Path;

        // Act
        await middleware.Invoke(context);

        // Assert
        Assert.NotEqual(new PathString(originalPath), context.Request.Path);
        Assert.Equal(new PathString(unEncodedHeaderValue), context.Request.Path);
        mockNext.Verify(next => next(context), Times.Once());
    }

    [Fact]
    public async Task Invoke_WithoutUnencodedUrlHeader_DoesNotChangeRequestPath()
    {
        // Arrange
        const string requestPathValue = "/cloud/devdiv";
        var context = CreateHttpContextWithHeader(requestPathValue);
        var mockNext = new Mock<RequestDelegate>();
        var middleware = new RestoreRawRequestPathMiddleware(mockNext.Object);
        var originalPath = context.Request.Path;

        // Act
        await middleware.Invoke(context);

        // Assert
        Assert.Equal(originalPath, context.Request.Path);
        mockNext.Verify(next => next(context), Times.Once());
    }

    private static HttpContext CreateHttpContextWithHeader(string requestPathValue, string unEncodedHeaderValue = null)
    {
        var context = new DefaultHttpContext
        {
            Request =
            {
                Path = requestPathValue
            }
        };

        if (unEncodedHeaderValue is not null)
        {
            context.Request.Headers[RestoreRawRequestPathMiddleware.UnEncodedUrlPathHeaderName] = unEncodedHeaderValue;
        }

        return context;
    }
}
