// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Reflection;
using Azure.Functions.Host.Controllers;
using Azure.Functions.Host.Models;
using Azure.Functions.Host.WorkerLink;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Azure.Functions.Host.Tests;

/// <summary>
/// Verifies that the worker-link route and DTOs remain isolated to the compute host.
/// </summary>
public class WorkerLinkContractTests
{
    [Fact]
    public void WorkerLinkTypes_LiveInComputeProductAssembly()
    {
        Assembly compute = typeof(ClientWorkerComposition).Assembly;
        Assert.Equal("Azure.Functions.Host", compute.GetName().Name);

        Assert.Same(compute, typeof(WorkerLinkController).Assembly);
        Assert.Same(compute, typeof(WorkerLinkRequest).Assembly);
        Assert.Same(compute, typeof(WorkerLinkError).Assembly);
        Assert.Same(compute, typeof(WorkerLinkErrorResponse).Assembly);
        Assert.Same(compute, typeof(RequestValidationError).Assembly);
        Assert.Same(compute, typeof(RequestValidationResponse).Assembly);
    }

    [Fact]
    public void StandardWebHost_DoesNotDefineWorkerLinkTypes()
    {
        // FunctionsHost is the shared/standard WebHost entry point; use it to reach that assembly.
        Assembly standardWebHost = typeof(FunctionsHost).Assembly;
        Assert.Equal("Microsoft.Azure.WebJobs.Script.WebHost", standardWebHost.GetName().Name);

        Assert.Null(standardWebHost.GetType("Azure.Functions.Host.Controllers.WorkerLinkController"));
        Assert.Null(standardWebHost.GetType("Azure.Functions.Host.WorkerLink.WorkerLinkRequest"));
        Assert.Null(standardWebHost.GetType("Azure.Functions.Host.WorkerLink.WorkerLinkError"));
        Assert.Null(standardWebHost.GetType("Azure.Functions.Host.WorkerLink.WorkerLinkErrorResponse"));
        Assert.Null(standardWebHost.GetType("Azure.Functions.Host.Models.RequestValidationError"));
        Assert.Null(standardWebHost.GetType("Azure.Functions.Host.Models.RequestValidationResponse"));
    }

    [Fact]
    public void WorkerLinkController_MapsPutAdminWorkersWithRouteIdentity()
    {
        MethodInfo link = typeof(WorkerLinkController).GetMethod(nameof(WorkerLinkController.LinkWorker))!;

        var httpPut = link.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpPutAttribute>();
        var route = link.GetCustomAttribute<Microsoft.AspNetCore.Mvc.RouteAttribute>();

        Assert.NotNull(httpPut);
        Assert.NotNull(route);
        Assert.Equal("admin/workers/{workerPodName}", route!.Template);
        ParameterInfo workerPodName = Assert.Single(link.GetParameters().Where(parameter =>
            string.Equals(parameter.Name, "workerPodName", StringComparison.Ordinal)));
        Assert.NotNull(workerPodName.GetCustomAttribute<Microsoft.AspNetCore.Mvc.FromRouteAttribute>());
    }

    [Fact]
    public void WorkerLinkErrorResponse_WithoutDetail_SerializesOnlyCode()
    {
        WorkerLinkErrorResponse response = new(new WorkerLinkError("LinkConflict"));

        string json = JsonConvert.SerializeObject(response);

        Assert.Equal("""{"error":{"code":"LinkConflict"}}""", json);
    }

    [Fact]
    public void WorkerLinkRequest_Serialization_DoesNotIncludeBodyIdentity()
    {
        WorkerLinkRequest request = Assert.IsType<WorkerLinkRequest>(JsonConvert.DeserializeObject<WorkerLinkRequest>(
            """{"workerGrpcEndpoint":"http://worker-pod-abc123:5001","workerPodName":null}"""));

        JObject body = JObject.Parse(JsonConvert.SerializeObject(request));

        Assert.Equal("http://worker-pod-abc123:5001", request.WorkerGrpcEndpoint);
        Assert.Null(body.Property("workerPodName"));
    }
}
