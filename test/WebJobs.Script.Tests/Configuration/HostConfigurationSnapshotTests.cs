// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration;

public sealed class HostConfigurationSnapshotTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"HostConfigurationSnapshot-{Guid.NewGuid():N}");

    public HostConfigurationSnapshotTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Theory]
    [InlineData(null, false, false, "", null)]
    [InlineData(null, true, false, "", null)]
    [InlineData(null, false, true, "", null)]
    [InlineData(null, false, false, "powershell", null)]
    [InlineData(null, true, false, "powershell", null)]
    [InlineData("{}", false, false, "powershell", null)]
    [InlineData("{}", true, false, "powershell", null)]
    [InlineData("{}", false, true, "", null)]
    [InlineData("""{"version":"2.0","configurationProfile":"mcp-custom-handler"}""", false, false, "", null)]
    [InlineData("""{"version":"2.0","configurationProfile":"web-app-custom-handler","http":{"routePrefix":"custom"}}""", false, false, "", null)]
    [InlineData("""{"version":"2.0","configurationProfile":"default"}""", false, false, "", "mcp-custom-handler")]
    [InlineData("""{"version":"2.0","configurationProfile":"mcp-custom-handler"}""", false, false, "", "default")]
    [InlineData("""{"version":"2.0","extensions":{"unknown":{"flag":true,"values":[1,2.5,null,"value",{"deep":false}]}},"custom":{"date":"2026-01-01T00:00:00Z","empty":[]}}""", false, false, "", null)]
    public void Parse_MatchesFileDefaultsProfilesAndFlattening(
        string? content, bool readOnly, bool logicApp, string runtime, string? profile)
    {
        HostJsonFileConfigurationOptions options = new(new ScriptApplicationHostOptions
        {
            ScriptPath = _directory,
            IsFileSystemReadOnly = readOnly,
        })
        {
            IsLogicApp = logicApp,
            WorkerRuntime = runtime,
            ConfigProfile = profile,
        };
        HostConfigurationSnapshot snapshot = HostConfigurationSnapshot.Parse(content, options);
        IConfigurationRoot remote = new ConfigurationBuilder().Add(snapshot).Build();
        Assert.Empty(Directory.EnumerateFileSystemEntries(_directory));

        if (content is not null)
        {
            File.WriteAllText(Path.Combine(_directory, "host.json"), content);
        }

        IConfigurationRoot local = new ConfigurationBuilder().Add(new HostJsonFileConfigurationSource(
            options, NullLoggerFactory.Instance, Mock.Of<IMetricsLogger>())).Build();

        Assert.Equal(
            local.AsEnumerable().OrderBy(pair => pair.Key, StringComparer.Ordinal),
            remote.AsEnumerable().OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"customer-secret\"")]
    [InlineData("{\"customer-secret\"")]
    [InlineData("""{"version":"customer-secret"}""")]
    [InlineData("""{"version":{"customer-secret":true}}""")]
    [InlineData("""{"version":["customer-secret"]}""")]
    [InlineData("""{"configurationProfile":"customer-secret"}""")]
    [InlineData("""{"version":"2.0","configurationProfile":"customer-secret"}""")]
    [InlineData("""{"version":"2.0","configurationProfile":{"customer-secret":true}}""")]
    public void Parse_InvalidConfigurationThrowsWithoutContentOrParserException(string content)
    {
        HostConfigurationException exception = Assert.Throws<HostConfigurationException>(() =>
            HostConfigurationSnapshot.Parse(content, new(new ScriptApplicationHostOptions { ScriptPath = _directory })));

        Assert.Null(exception.InnerException);
        Assert.Equal(
            "The Host configuration is not valid JSON with a supported schema version and configuration profile.",
            exception.Message);
        Assert.DoesNotContain("customer-secret", exception.ToString(), StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_directory));
    }

    [Fact]
    public void Parse_InvalidBootstrapProfileThrowsWithoutContent()
    {
        HostJsonFileConfigurationOptions options = new(new ScriptApplicationHostOptions()) { ConfigProfile = "customer-secret" };

        HostConfigurationException exception = Assert.Throws<HostConfigurationException>(() =>
            HostConfigurationSnapshot.Parse("""{"version":"2.0"}""", options));

        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("customer-secret", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DoesNotExposeMutableSnapshotOrReadFiles()
    {
        HostConfigurationSnapshot snapshot = HostConfigurationSnapshot.Parse(
            """{"version":"2.0","extensions":{"custom":{"setting":"worker"}}}""",
            new(new ScriptApplicationHostOptions { ScriptPath = _directory }));
        IConfigurationRoot first = new ConfigurationBuilder().Add(snapshot).Build();
        first["AzureFunctionsJobHost:extensions:custom:setting"] = "child-only";
        File.WriteAllText(Path.Combine(_directory, "host.json"), "invalid local document");

        IConfigurationRoot restarted = new ConfigurationBuilder().Add(snapshot).Build();
        restarted.Reload();

        Assert.Equal("child-only", first["AzureFunctionsJobHost:extensions:custom:setting"]);
        Assert.Equal("worker", restarted["AzureFunctionsJobHost:extensions:custom:setting"]);
    }

    [Fact]
    public void Build_PreservesOverridesAndUnknownSections()
    {
        HostConfigurationSnapshot snapshot = HostConfigurationSnapshot.Parse(
            """{"version":"2.0","configurationProfile":"mcp-custom-handler","http":{"routePrefix":"worker"},"extensions":{"unknown":{"values":[true,2,null]}}}""",
            new(new ScriptApplicationHostOptions()));
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .Add(snapshot)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureFunctionsJobHost:http:routePrefix"] = "environment",
            })
            .Build();

        Assert.Equal("environment", configuration["AzureFunctionsJobHost:http:routePrefix"]);
        Assert.Equal("true", configuration["AzureFunctionsJobHost:customHandler:enableProxyingHttpRequest"]);
        Assert.Equal("True", configuration["AzureFunctionsJobHost:extensions:unknown:values:0"]);
        Assert.Equal("2", configuration["AzureFunctionsJobHost:extensions:unknown:values:1"]);
        Assert.Equal(string.Empty, configuration["AzureFunctionsJobHost:extensions:unknown:values:2"]);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }
}
