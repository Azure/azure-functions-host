// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.WebJobs.Script.Tests;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests;

[Trait(TestTraits.Group, TestTraits.NonE2ESpecialization)]
public class PlaceholderWarmupTests
{
    [Theory]
    [InlineData("GET", "WarmUp")]
    [InlineData("POST", "CSharpHttpWarmup")]
    public async Task Warmup_ExecutesPrecompiledFunctionWithoutScript(string method, string route)
    {
        StandbyManager.ResetChangeToken();
        using var root = new TempDirectory();
        var environment = new TestEnvironment(new Dictionary<string, string>
        {
            [EnvironmentSettingNames.AzureWebsitePlaceholderMode] = "1",
            [EnvironmentSettingNames.AzureWebsiteInstanceId] = Guid.NewGuid().ToString(),
            [EnvironmentSettingNames.AzureWebsiteHomePath] = root.Path,
            [EnvironmentSettingNames.AzureWebsiteContainerReady] = "0"
        });
        using var host = Program.CreateHostBuilder()
            .ConfigureWebHost(builder => builder.UseTestServer())
            .ConfigureServices(services =>
            {
                services.AddSingleton<IEnvironment>(environment);
                services.PostConfigure<ScriptApplicationHostOptions>(options =>
                {
                    if (options.IsStandbyConfiguration)
                    {
                        options.ScriptPath = root.Path;
                        options.LogPath = Path.Combine(root.Path, "logs");
                    }
                });
            })
            .Build();

        try
        {
            await host.StartAsync();
            using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/{route}");
            if (string.Equals(method, "POST", StringComparison.Ordinal))
            {
                request.Content = new StringContent("{\"name\":\"placeholder\"}");
            }

            using var response = await host.GetTestClient().SendAsync(request);

            Assert.True(response.StatusCode == HttpStatusCode.OK,
                $"Warmup returned {response.StatusCode}. Host startup error: {host.Services.GetRequiredService<IScriptHostManager>().LastError}");
            Assert.Equal("WarmUp complete.", await response.Content.ReadAsStringAsync());
            Assert.Empty(Directory.GetFiles(root.Path, "*.csx", SearchOption.AllDirectories));
            Assert.Empty(Directory.GetFiles(root.Path, "function.json", SearchOption.AllDirectories));
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
