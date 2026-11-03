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
    [InlineData("""{"version":"2.0","configurationProfile":"mcp-custom-handler"}""", null)]
    [InlineData("""{"version":"2.0","configurationProfile":"web-app-custom-handler","http":{"routePrefix":"custom"}}""", null)]
    [InlineData("""{"version":"2.0","configurationProfile":"default"}""", "mcp-custom-handler")]
    [InlineData("""{"version":"2.0","configurationProfile":"mcp-custom-handler"}""", "default")]
    [InlineData("""{"version":"2.0","extensions":{"unknown":{"flag":true,"values":[1,2.5,null,"value",{"deep":false}]}},"custom":{"date":"2026-01-01T00:00:00Z","empty":[]}}""", null)]
    [InlineData("""{"version":"2.0","custom":{"undefined":"undefined","values":["NaN","Infinity","-Infinity"],"duplicate":"first","duplicate":"last"}}""", null)]
    public void Parse_MatchesFileProfilesAndFlattening(string content, string? profile)
    {
        HostJsonFileConfigurationOptions options = new(new ScriptApplicationHostOptions { ScriptPath = _directory })
        {
            ConfigProfile = profile,
        };
        HostConfigurationSnapshot snapshot = HostConfigurationSnapshot.Parse(content, profile);
        IConfigurationRoot remote = new ConfigurationBuilder().Add(snapshot).Build();
        Assert.Empty(Directory.EnumerateFileSystemEntries(_directory));

        File.WriteAllText(Path.Combine(_directory, "host.json"), content);
        IConfigurationRoot local = new ConfigurationBuilder().Add(new HostJsonFileConfigurationSource(
            options, NullLoggerFactory.Instance, Mock.Of<IMetricsLogger>())).Build();

        Assert.Equal(
            local.AsEnumerable().OrderBy(pair => pair.Key, StringComparer.Ordinal),
            remote.AsEnumerable().OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(null, false, false, "")]
    [InlineData(null, true, false, "")]
    [InlineData(null, false, true, "")]
    [InlineData(null, true, true, "powershell")]
    [InlineData(null, false, false, "powershell")]
    [InlineData(null, true, false, "powershell")]
    [InlineData("{}", false, false, "powershell")]
    [InlineData("{}", true, false, "powershell")]
    [InlineData("{}", false, true, "")]
    public void Parse_LeavesBootstrapDefaultsExclusiveToFileLoading(
        string? content, bool readOnly, bool logicApp, string runtime)
    {
        IConfigurationRoot delivered = new ConfigurationBuilder().Add(HostConfigurationSnapshot.Parse(content)).Build();

        Assert.Empty(Directory.EnumerateFileSystemEntries(_directory));
        Assert.Equal("default", delivered["AzureFunctionsJobHost:configurationProfile"]);
        Assert.Null(delivered["AzureFunctionsJobHost:version"]);
        Assert.Null(delivered["AzureFunctionsJobHost:isDefaultHostConfig"]);
        Assert.Null(delivered["AzureFunctionsJobHost:extensionBundle:id"]);
        Assert.Null(delivered["AzureFunctionsJobHost:managedDependency:Enabled"]);

        if (content is not null)
        {
            File.WriteAllText(Path.Combine(_directory, "host.json"), content);
        }

        HostJsonFileConfigurationOptions options = new(new ScriptApplicationHostOptions
        {
            ScriptPath = _directory,
            IsFileSystemReadOnly = readOnly,
        })
        {
            IsLogicApp = logicApp,
            WorkerRuntime = runtime,
        };
        IConfigurationRoot local = new ConfigurationBuilder().Add(new HostJsonFileConfigurationSource(
            options, NullLoggerFactory.Instance, Mock.Of<IMetricsLogger>())).Build();

        Assert.Equal("2.0", local["AzureFunctionsJobHost:version"]);
        Assert.Equal("True", local["AzureFunctionsJobHost:isDefaultHostConfig"]);
        Assert.Equal(!readOnly && string.Equals(runtime, "powershell", StringComparison.Ordinal) ? "True" : null,
            local["AzureFunctionsJobHost:managedDependency:Enabled"]);
        string? expectedBundle = content is null && !readOnly
            ? logicApp ? ScriptConstants.WorkFlowExtensionBundleId : ScriptConstants.DefaultExtensionBundleId
            : null;
        Assert.Equal(expectedBundle, local["AzureFunctionsJobHost:extensionBundle:id"]);
    }

    [Theory]
    [InlineData("""{"logging":{"logLevel":{"default":"Information"}}}""")]
    [InlineData("""{"version":"3.0","logging":{"logLevel":{"default":"Information"}}}""")]
    public void Parse_DoesNotRequireFileSchemaVersion(string content)
    {
        IConfigurationRoot delivered = new ConfigurationBuilder().Add(HostConfigurationSnapshot.Parse(content)).Build();

        Assert.Equal("Information", delivered["AzureFunctionsJobHost:logging:logLevel:default"]);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_directory));
    }

    [Fact]
    public void Parse_PreservesExplicitBundleAndManagedDependencySettings()
    {
        HostConfigurationSnapshot snapshot = HostConfigurationSnapshot.Parse("""
            {
                "extensionBundle": { "id": "explicit-bundle", "version": "[4.*, 5.0.0)" },
                "managedDependency": { "Enabled": true }
            }
            """);

        IConfigurationRoot delivered = new ConfigurationBuilder().Add(snapshot).Build();

        Assert.Equal("explicit-bundle", delivered["AzureFunctionsJobHost:extensionBundle:id"]);
        Assert.Equal("[4.*, 5.0.0)", delivered["AzureFunctionsJobHost:extensionBundle:version"]);
        Assert.Equal("True", delivered["AzureFunctionsJobHost:managedDependency:Enabled"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"customer-secret\"")]
    [InlineData("{\"customer-secret\"")]
    [InlineData("""{"configurationProfile":"customer-secret"}""")]
    [InlineData("""{"configurationProfile":{"customer-secret":true}}""")]
    [InlineData("""{"configurationProfile":["customer-secret"]}""")]
    public void Parse_InvalidConfigurationThrowsWithoutContentOrParserException(string content)
    {
        HostConfigurationException exception = Assert.Throws<HostConfigurationException>(() =>
            HostConfigurationSnapshot.Parse(content));

        Assert.Null(exception.InnerException);
        Assert.Equal(
            "The Host configuration must be a JSON object with a supported configuration profile.",
            exception.Message);
        Assert.DoesNotContain("customer-secret", exception.ToString(), StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_directory));
    }

    [Theory]
    [InlineData("""{"version":"2.0","functionTimeout":undefined,"custom":"customer-secret"}""")]
    [InlineData("""{"version":"2.0","custom":{"customer-secret":[undefined]}}""")]
    [InlineData("""{"version":"2.0","custom":{"customer-secret":NaN}}""")]
    [InlineData("""{"version":"2.0","custom":{"customer-secret":Infinity}}""")]
    [InlineData("""{"version":"2.0","custom":{"customer-secret":-Infinity}}""")]
    [InlineData("""{'version':'2.0','custom':'customer-secret'}""")]
    [InlineData("""{"version":"2.0",custom:"customer-secret"}""")]
    [InlineData("""{"version":"2.0",/* customer-secret */"custom":true}""")]
    [InlineData("""{"version":"2.0","custom":"customer-secret",}""")]
    public void Parse_RejectsNonJsonSyntaxWithoutChangingFileLoading(string content)
    {
        string path = Path.Combine(_directory, "host.json");
        File.WriteAllText(path, content);
        HostJsonFileConfigurationOptions options = new(new ScriptApplicationHostOptions
        {
            ScriptPath = _directory,
            IsFileSystemReadOnly = true,
        });
        IConfigurationRoot local = new ConfigurationBuilder().Add(new HostJsonFileConfigurationSource(
            options, NullLoggerFactory.Instance, Mock.Of<IMetricsLogger>())).Build();

        Assert.Equal("2.0", local["AzureFunctionsJobHost:version"]);

        HostConfigurationException exception = Assert.Throws<HostConfigurationException>(() =>
            HostConfigurationSnapshot.Parse(content));

        Assert.Equal(
            "The Host configuration must be a JSON object with a supported configuration profile.",
            exception.Message);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("customer-secret", exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(content, File.ReadAllText(path));
    }

    [Fact]
    public void Parse_InvalidBootstrapProfileThrowsWithoutContent()
    {
        HostConfigurationException exception = Assert.Throws<HostConfigurationException>(() =>
            HostConfigurationSnapshot.Parse("{}", "customer-secret"));

        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("customer-secret", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DoesNotExposeMutableSnapshotOrReadFiles()
    {
        HostConfigurationSnapshot snapshot = HostConfigurationSnapshot.Parse(
            """{"extensions":{"custom":{"setting":"worker"}}}""");
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
            """{"configurationProfile":"mcp-custom-handler","http":{"routePrefix":"worker"},"extensions":{"unknown":{"values":[true,2,null]}}}""");
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
