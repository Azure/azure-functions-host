// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd;

[Trait(TestTraits.Category, TestTraits.EndToEnd)]
public sealed class WebFunctionEndToEndTests
{
    [Theory]
    [InlineData("index.js", null, "node")]
    [InlineData("server with spaces.js", "server with spaces.js", "node")]
    [InlineData("index.js", null, null)]
    [InlineData("index.js", null, "custom")]
    public async Task WebFunction_WithoutHostJson_ProxiesRequestsAndStreams(string fileName, string entryPoint, string runtime)
    {
        using TempDirectory app = new();
        string sample = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "sample", "WebFunction", "index.js"));
        File.Copy(sample, Path.Combine(app.Path, fileName));

        using TestScopedSettings settings = new(ScriptSettingsManager.Instance, new Dictionary<string, string>
        {
            ["AzureFunctionsJobHost__configurationProfile"] = "web-function",
            [EnvironmentSettingNames.FunctionWorkerRuntime] = runtime,
            ["FUNCTIONS_WEB_ENTRYPOINT"] = entryPoint
        });

        await using TestFunctionHost host = new(
            app.Path, Path.Combine(Path.GetTempPath(), "WebFunctionE2E"), addTestSettings: false, addStorageExtensions: false);

        using HttpResponseMessage response = await host.HttpClient.PostAsync(
            "/echo?name=Azure", new StringContent("hello", Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("POST", body.RootElement.GetProperty("method").GetString());
        Assert.Equal("/echo?name=Azure", body.RootElement.GetProperty("path").GetString());
        Assert.Equal("hello", body.RootElement.GetProperty("body").GetString());
        Assert.True(Guid.TryParse(body.RootElement.GetProperty("requestId").GetString(), out _));
        Assert.False(File.Exists(Path.Combine(app.Path, "host.json")));

        using HttpResponseMessage customMethodResponse = await host.HttpClient.SendAsync(
            new HttpRequestMessage(new HttpMethod("PROPFIND"), "/resource"));
        Assert.Equal(HttpStatusCode.OK, customMethodResponse.StatusCode);
        using JsonDocument customMethodBody = JsonDocument.Parse(await customMethodResponse.Content.ReadAsStringAsync());
        Assert.Equal("PROPFIND", customMethodBody.RootElement.GetProperty("method").GetString());

        using (HttpResponseMessage streamResponse = await host.HttpClient.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/events"), HttpCompletionOption.ResponseHeadersRead))
        {
            Assert.Equal(HttpStatusCode.OK, streamResponse.StatusCode);
            Assert.Equal("text/event-stream", streamResponse.Content.Headers.ContentType.MediaType);
            using StreamReader reader = new(await streamResponse.Content.ReadAsStreamAsync());
            Assert.Equal("data: connected", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(string.Empty, await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.StartsWith("data: ", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));

            using JsonDocument streams = JsonDocument.Parse(await host.HttpClient.GetStringAsync("/streams"));
            Assert.Equal(1, streams.RootElement.GetProperty("activeStreams").GetInt32());
        }

        await TestHelpers.Await(async () =>
        {
            using JsonDocument streams = JsonDocument.Parse(await host.HttpClient.GetStringAsync("/streams"));
            return streams.RootElement.GetProperty("activeStreams").GetInt32() is 0;
        }, timeout: 10000, userMessageCallback: host.GetLog);
    }

    [Fact]
    public async Task WebFunction_NodePackage_UsesStartScript()
    {
        using TempDirectory app = new();
        string sample = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "sample", "WebFunction", "index.js"));
        File.Copy(sample, Path.Combine(app.Path, "server with spaces.js"));
        File.WriteAllText(Path.Combine(app.Path, "package.json"),
            """{"scripts":{"start":"node \"server with spaces.js\""}}""");
        using TestScopedSettings settings = new(ScriptSettingsManager.Instance, new Dictionary<string, string>
        {
            ["AzureFunctionsJobHost__configurationProfile"] = "web-function",
            [EnvironmentSettingNames.FunctionWorkerRuntime] = "node"
        });
        await using TestFunctionHost host = new(
            app.Path, Path.Combine(Path.GetTempPath(), "WebFunctionE2E"), addTestSettings: false, addStorageExtensions: false);

        using HttpResponseMessage response = await host.HttpClient.PostAsync(
            "/echo?name=Azure", new StringContent("hello", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("POST", body.RootElement.GetProperty("method").GetString());
        Assert.Equal("/echo?name=Azure", body.RootElement.GetProperty("path").GetString());
        Assert.Equal("hello", body.RootElement.GetProperty("body").GetString());
        Assert.False(File.Exists(Path.Combine(app.Path, "host.json")));
    }

    [Fact]
    public async Task WebFunction_Dispose_TerminatesChildProcesses()
    {
        using TempDirectory app = new();
        File.WriteAllText(Path.Combine(app.Path, "parent.js"), """
            const fs = require('node:fs');
            const { spawn } = require('node:child_process');
            const child = spawn(process.execPath, ['-e', 'setInterval(() => {}, 1000)'], { stdio: 'ignore' });
            fs.writeFileSync('child.tmp', String(child.pid));
            fs.renameSync('child.tmp', 'child.pid');
            setInterval(() => {}, 1000);
            """);

        TestEnvironment environment = new();
        HttpWorkerOptions options = new()
        {
            IsWebFunction = true,
            Description = new HttpWorkerDescription(),
            Arguments = new WorkerProcessArguments()
        };
        HttpWorkerProcess worker = new(
            "test", app.Path, options, new ScriptEventManager(),
            new DefaultWorkerProcessFactory(environment, NullLoggerFactory.Instance),
            new EmptyProcessRegistry(), NullLogger.Instance, Mock.Of<IWorkerConsoleLogSource>(),
            environment, new TestMetricsLogger(), Mock.Of<IServiceProvider>(),
            new TestOptionsMonitor<ScriptApplicationHostOptions>(), NullLoggerFactory.Instance);

        Process child = null;
        try
        {
            ProcessStartInfo startInfo = new("node")
            {
                WorkingDirectory = app.Path,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("parent.js");
            worker.Process = Process.Start(startInfo);
            string childPidPath = Path.Combine(app.Path, "child.pid");
            await TestHelpers.Await(() => Task.FromResult(File.Exists(childPidPath)), timeout: 10000);
            child = Process.GetProcessById(int.Parse(File.ReadAllText(childPidPath), CultureInfo.InvariantCulture));
            Assert.False(child.HasExited);
        }
        finally
        {
            worker.Dispose();
        }

        using (child)
        {
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}
