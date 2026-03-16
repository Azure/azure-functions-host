// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Features;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Routing;

public class ScriptRouteHandlerTests
{
    private readonly Mock<IScriptJobHost> _mockHost;
    private readonly ScriptRouteHandler _handler;

    public ScriptRouteHandlerTests()
    {
        _mockHost = new Mock<IScriptJobHost>();
        _mockHost.SetupGet(h => h.Functions).Returns(new List<FunctionDescriptor>());

        _handler = new ScriptRouteHandler(
            new LoggerFactory(),
            _mockHost.Object,
            SystemEnvironment.Instance,
            isProxy: false);
    }

    [Fact]
    public async Task InvokeAsync_SentinelName_SetsHeadNotAllowedFeature()
    {
        var context = new DefaultHttpContext();
        string sentinelName = ScriptConstants.HeadMethodNotAllowedPrefix + "GET, POST";

        await _handler.InvokeAsync(context, sentinelName);

        var feature = context.Features.Get<IHeadNotAllowedFeature>();
        Assert.NotNull(feature);
        Assert.Equal("GET, POST", feature.AllowedMethods);
    }

    [Fact]
    public async Task InvokeAsync_SentinelName_DoesNotSetFunctionExecutionFeature()
    {
        var context = new DefaultHttpContext();
        string sentinelName = ScriptConstants.HeadMethodNotAllowedPrefix + "POST";

        await _handler.InvokeAsync(context, sentinelName);

        Assert.Null(context.Features.Get<IFunctionExecutionFeature>());
    }

    [Fact]
    public async Task InvokeAsync_SentinelName_ExtractsAllowedMethodsCorrectly()
    {
        var context = new DefaultHttpContext();
        await _handler.InvokeAsync(context, ScriptConstants.HeadMethodNotAllowedPrefix + "DELETE");

        var feature = context.Features.Get<IHeadNotAllowedFeature>();
        Assert.NotNull(feature);
        Assert.Equal("DELETE", feature.AllowedMethods);
    }
}
