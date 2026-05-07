// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Http;
using Xunit;

namespace Microsoft.Azure.Functions.WorkerProxy.Tests;

public class WorkerProxyHttpRequestLogContextTests
{
    [Fact]
    public void Create_IncludesPathQueryAndCorrelationHeaders()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.PathBase = "/root";
        httpContext.Request.Path = "/api/HttpTrigger";
        httpContext.Request.QueryString = new QueryString("?name=test");
        httpContext.Request.Headers["x-ms-invocation-id"] = "inv-123";
        httpContext.Request.Headers["traceparent"] = "00-abc";
        httpContext.Request.Headers["x-ms-request-id"] = "req-456";

        var logContext = WorkerProxyHttpRequestLogContext.Create(httpContext.Request, "http://127.0.0.1:5001");

        Assert.Equal("POST", logContext.Method);
        Assert.Equal("/root/api/HttpTrigger?name=test", logContext.Path);
        Assert.Equal("http://127.0.0.1:5001", logContext.Destination);
        Assert.Equal("inv-123", logContext.InvocationId);
        Assert.Equal("00-abc", logContext.TraceParent);
        Assert.Equal("req-456", logContext.RequestId);
    }

    [Fact]
    public void Create_UsesPlaceholderForMissingCorrelationHeaders()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Path = "/api/HttpTrigger";
        httpContext.Request.Headers["x-ms-invocation-id"] = "   ";

        var logContext = WorkerProxyHttpRequestLogContext.Create(httpContext.Request, "http://127.0.0.1:5001");

        Assert.Equal("<none>", logContext.InvocationId);
        Assert.Equal("<none>", logContext.TraceParent);
        Assert.Equal("<none>", logContext.RequestId);
    }
}
