// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public class WorkerProxyApplicationTests
{
    [Fact]
    public async Task ManagementListener_MapsOnlyStartupProbe()
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));

        using HttpResponseMessage readyResponse = await client.GetAsync("/admin/instance/ready", timeout.Token);
        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);
        Assert.Empty(await readyResponse.Content.ReadAsByteArrayAsync(timeout.Token));

        using HttpResponseMessage unsupportedMethodResponse = await client.PostAsync("/admin/instance/ready", content: null, timeout.Token);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, unsupportedMethodResponse.StatusCode);

        using HttpResponseMessage unrelatedRouteResponse = await client.GetAsync("/admin/worker/ready", timeout.Token);
        Assert.Equal(HttpStatusCode.NotFound, unrelatedRouteResponse.StatusCode);
    }
}
