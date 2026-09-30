// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure.Functions.Host.WorkerLink;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Azure.Functions.Host.Tests.WorkerLink;

public sealed class WorkerLinkSpecializationWaitExemptionTests
{
    [Theory]
    [InlineData("PUT", "/admin/workers/worker-pod-1", true)]
    [InlineData("put", "/ADMIN/Workers/worker-pod-1", true)]
    [InlineData("PUT", "/admin/workers/worker-pod-1/", true)]
    [InlineData("PUT", "/admin/workers/worker-pod-1/extra", false)]
    [InlineData("PUT", "/admin/workers/worker-pod-1//", false)]
    [InlineData("PUT", "/admin/workers//", false)]
    [InlineData("POST", "/admin/workers/worker-pod-1", false)]
    [InlineData("GET", "/admin/workers/worker-pod-1", false)]
    [InlineData("DELETE", "/admin/workers/worker-pod-1", false)]
    [InlineData("PUT", "/admin/workers", false)]
    [InlineData("PUT", "/admin/workers/", false)]
    [InlineData("PUT", "/admin/workersx/worker-pod-1", false)]
    [InlineData("PUT", "/admin/instance/assign", false)]
    [InlineData("PUT", "/api/admin/workers/worker-pod-1", false)]
    [InlineData("PUT", "/", false)]
    public void IsExempt_MatchesOnlyWorkerLinkPut(string method, string path, bool expected)
    {
        DefaultHttpContext context = new();
        context.Request.Method = method;
        context.Request.Path = path;

        Assert.Equal(expected, new WorkerLinkSpecializationWaitExemption().IsExempt(context.Request));
    }
}
