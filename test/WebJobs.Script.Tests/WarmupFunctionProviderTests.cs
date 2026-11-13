// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests;

public class WarmupFunctionProviderTests
{
    [Theory]
    [InlineData(true, true, 1)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    public async Task GetFunctionMetadataAsync_OnlyConventionalPlaceholder(bool standby, bool supportsScriptHost, int expected)
    {
        var application = new Mock<IOptionsMonitor<ScriptApplicationHostOptions>>();
        application.SetupGet(o => o.CurrentValue).Returns(new ScriptApplicationHostOptions
        {
            IsStandbyConfiguration = standby,
            ScriptPath = Path.GetTempPath()
        });
        var options = new Mock<IOptionsMonitor<StandbyOptions>>();
        options.SetupGet(o => o.CurrentValue).Returns(new StandbyOptions { SupportsPlaceholderScriptHost = supportsScriptHost });
        var provider = new WarmupFunctionProvider(application.Object, options.Object);

        var metadata = await provider.GetFunctionMetadataAsync();

        Assert.Equal(expected, metadata.Length);
        if (standby && supportsScriptHost)
        {
            Assert.StartsWith("assembly:", metadata[0].ScriptFile);
            Assert.Equal("{x:regex(^(warmup|csharphttpwarmup)$)}", metadata[0].Bindings[0].Raw["route"]);
            application.SetupGet(o => o.CurrentValue).Returns(new ScriptApplicationHostOptions { IsStandbyConfiguration = false });
            Assert.Empty(await provider.GetFunctionMetadataAsync());
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"name\":\"placeholder\"}")]
    public async Task Run_PreservesWarmupResponse(string body)
    {
        var request = new DefaultHttpContext().Request;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
        request.Body = stream;

        var result = await WarmupFunctionProvider.Run(request, NullLogger.Instance);

        Assert.Equal("WarmUp complete.", Assert.IsType<OkObjectResult>(result).Value);
    }
}
