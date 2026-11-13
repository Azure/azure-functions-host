// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Http;

public sealed class WebFunctionWorkerDescriptionResolverTests : IDisposable
{
    private readonly TempDirectory _app = new(Path.Combine(Path.GetTempPath(), $"web function {Guid.NewGuid():N}"));

    [Fact]
    public void Resolve_Node_UsesPackageMain()
    {
        WriteFile("package.json", """{"main":"dist/server.js"}""");
        WriteFile(Path.Combine("dist", "server.js"));
        WriteFile("index.js");

        HttpWorkerDescription result = Resolve("node", null, null, out WebFunctionLanguage language);

        Assert.Equal(WebFunctionLanguage.Node, language);
        Assert.Equal("node", result.DefaultExecutablePath);
        Assert.Equal("dist/server.js", result.DefaultWorkerPath);
    }

    [Fact]
    public void Resolve_Node_EntryPointOverridesPackageMain()
    {
        WriteFile("package.json", """{"main":"missing.js","scripts":{"start":"node missing.js"}}""");
        WriteFile("server with spaces.js");

        HttpWorkerDescription result = Resolve("NODE", "server with spaces.js", null, out _);

        Assert.Equal("server with spaces.js", result.DefaultWorkerPath);
    }

    [Fact]
    public void Resolve_Node_PrefersPackageStartScript()
    {
        WriteFile("package.json", """{"main":"missing.js","scripts":{"start":"node server.js"}}""");

        HttpWorkerDescription result = Resolve("node", null, null, out _);

        Assert.Equal(OperatingSystem.IsWindows() ? "cmd.exe" : "npm", result.DefaultExecutablePath);
        Assert.Equal(
            OperatingSystem.IsWindows() ? new[] { "/d", "/s", "/c", "npm.cmd", "run", "start" } : new[] { "run", "start" },
            result.Arguments);
        Assert.Null(result.DefaultWorkerPath);
    }

    [Theory]
    [InlineData("index.js")]
    [InlineData("server.mjs")]
    [InlineData("app.cjs")]
    public void Resolve_Node_DetectsConventionalEntryPoint(string fileName)
    {
        WriteFile(fileName);

        HttpWorkerDescription result = Resolve("node", null, null, out _);

        Assert.Equal(fileName, result.DefaultWorkerPath);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"main":42}""")]
    [InlineData("""{"main":"missing.js"}""")]
    [InlineData("""{"scripts":"invalid"}""")]
    [InlineData("""{"scripts":{"start":42}}""")]
    [InlineData("""{"scripts":{"start":""}}""")]
    public void Resolve_Node_InvalidPackage_FailsExplicitly(string package)
    {
        WriteFile("package.json", package);
        WriteFile("index.js");

        Assert.Throws<HostConfigurationException>(() => Resolve("node", null, null, out _));
    }

    [Fact]
    public void Resolve_Python_UsesWsgiServerAndAssignedPort()
    {
        WriteFile("app.py");

        HttpWorkerDescription result = Resolve("python", null, null, out WebFunctionLanguage language);

        Assert.Equal(WebFunctionLanguage.Python, language);
        Assert.Equal("python", result.DefaultExecutablePath);
        Assert.Equal("app:app", result.DefaultWorkerPath);
        Assert.False(result.UseStdErrorStreamForErrorsOnly);
        Assert.Equal("-m", result.Arguments[0]);
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("waitress", result.Arguments[1]);
            Assert.Contains("--listen=127.0.0.1:8123", result.Arguments);
        }
        else
        {
            Assert.Equal("gunicorn", result.Arguments[1]);
            Assert.Contains("127.0.0.1:8123", result.Arguments);
            Assert.Contains("gthread", result.Arguments);
            Assert.Contains("8", result.Arguments);
        }
    }

    [Fact]
    public void Resolve_Python_AsgiUsesUvicornAndConfiguredEntryPoint()
    {
        HttpWorkerDescription result = Resolve("python", "project.server:application", "asgi", out _);

        Assert.Equal("project.server:application", result.DefaultWorkerPath);
        Assert.Equal(new[] { "-m", "uvicorn", "--host", "127.0.0.1", "--port", "8123" }, result.Arguments);
        Assert.False(result.UseStdErrorStreamForErrorsOnly);
    }

    [Theory]
    [InlineData("missing-colon", "wsgi")]
    [InlineData("module:app", "invalid")]
    [InlineData("module:\"app\"", "wsgi")]
    public void Resolve_Python_InvalidSettingsFailExplicitly(string entryPoint, string protocol)
    {
        Assert.Throws<HostConfigurationException>(() => Resolve("python", entryPoint, protocol, out _));
    }

    [Fact]
    public void Resolve_Java_UsesSingleRunnableArchive()
    {
        WriteFile(Path.Combine("build", "libs", "app.jar"));
        WriteFile(Path.Combine("build", "libs", "app-plain.jar"));

        HttpWorkerDescription result = Resolve("java", null, null, out WebFunctionLanguage language);

        Assert.Equal(WebFunctionLanguage.Java, language);
        Assert.Equal("java", result.DefaultExecutablePath);
        Assert.Equal(new[] { "-jar" }, result.Arguments);
        Assert.Equal(Path.Combine("build", "libs", "app.jar"), result.DefaultWorkerPath);
    }

    [Fact]
    public void Resolve_Java_RequiresOverrideForMultipleArchives()
    {
        WriteFile("one.jar");
        WriteFile("two.jar");

        Assert.Throws<HostConfigurationException>(() => Resolve("java", null, null, out _));
        HttpWorkerDescription result = Resolve("java", "two.jar", null, out _);
        Assert.Equal("two.jar", result.DefaultWorkerPath);
    }

    [Fact]
    public void Resolve_Java_UsesConfiguredJavaHome()
    {
        WriteFile("app.jar");
        string executable = Path.Combine("java home", "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
        WriteFile(executable);

        HttpWorkerDescription result = Resolve("java", null, null, out _, Path.Combine(_app.Path, "java home"));

        Assert.Equal(Path.Combine(_app.Path, executable), result.DefaultExecutablePath);
    }

    [Fact]
    public void Resolve_Java_InvalidJavaHomeFailsExplicitly()
    {
        WriteFile("app.jar");

        HostConfigurationException exception = Assert.Throws<HostConfigurationException>(
            () => Resolve("java", null, null, out _, Path.Combine(_app.Path, "missing")));

        Assert.Contains("JAVA_HOME", exception.Message);
    }

    [Fact]
    public void Resolve_AutodetectsSingleRuntime()
    {
        WriteFile("index.js");

        HttpWorkerDescription result = Resolve(null, null, null, out WebFunctionLanguage language);

        Assert.Equal(WebFunctionLanguage.Node, language);
        Assert.Equal("index.js", result.DefaultWorkerPath);
    }

    [Fact]
    public void Resolve_AmbiguousOrUnsupportedRuntimeFailsExplicitly()
    {
        WriteFile("index.js");
        WriteFile("app.py");

        Assert.Throws<HostConfigurationException>(() => Resolve(null, null, null, out _));
        Assert.Throws<HostConfigurationException>(() => Resolve("dotnet-isolated", null, null, out _));
    }

    [Fact]
    public void Configure_WebProfile_InfersDescriptionAndAnonymousRoute()
    {
        WriteFile("index.js");
        IConfiguration configuration = CreateWebConfiguration("node");
        HttpWorkerOptionsSetup setup = CreateSetup(configuration);
        HttpWorkerOptions options = new();

        setup.Configure(options);

        Assert.True(options.IsWebFunction);
        Assert.True(options.CustomRoutesEnabled);
        Assert.True(options.EnableProxyingHttpRequest);
        Assert.Equal(WebFunctionLanguage.Node, options.WebFunctionLanguage);
        Assert.Equal("node", options.Arguments.ExecutablePath);
        Assert.Equal("index.js", options.Arguments.WorkerPath);
        Assert.Equal(_app.Path, options.Description.WorkingDirectory);
        Assert.Equal(AuthorizationLevel.Anonymous, Assert.Single(options.Http.Routes).AuthorizationLevel);
    }

    [Fact]
    public void Configure_WebProfile_PreservesExplicitDescription()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .Add(HostConfigurationSnapshot.Parse(null, "web-function"))
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                [EnvironmentSettingNames.FunctionWorkerRuntime] = "node",
                ["AzureFunctionsJobHost:customHandler:description:defaultExecutablePath"] = "custom-server"
            })
            .Build();
        HttpWorkerOptionsSetup setup = CreateSetup(configuration);
        HttpWorkerOptions options = new();

        setup.Configure(options);

        Assert.Equal("custom-server", options.Arguments.ExecutablePath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Configure_WebProfile_InStandby_DoesNotConfigureWebApplication(bool hasExplicitDescription)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddConfiguration(CreateWebConfiguration("node"))
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["AzureFunctionsJobHost:customHandler:description:defaultExecutablePath"] =
                    hasExplicitDescription ? "custom-server" : null
            })
            .Build();
        HttpWorkerOptionsSetup setup = CreateSetup(configuration, standby: true);
        HttpWorkerOptions options = new();

        setup.Configure(options);

        Assert.True(options.IsWebFunction);
        Assert.Null(options.Description);
        Assert.Null(options.Http);
        options.Port = 0;
        Assert.True(setup.Validate(null, options).Succeeded);
    }

    public void Dispose()
    {
        _app.Dispose();
    }

    private HttpWorkerDescription Resolve(string runtime, string entryPoint, string protocol, out WebFunctionLanguage language, string javaHome = null)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                [EnvironmentSettingNames.FunctionWorkerRuntime] = runtime,
                [WebFunctionWorkerDescriptionResolver.EntryPointSetting] = entryPoint,
                [WebFunctionWorkerDescriptionResolver.PythonProtocolSetting] = protocol,
                ["JAVA_HOME"] = javaHome
            })
            .Build();

        return WebFunctionWorkerDescriptionResolver.Resolve(_app.Path, configuration, 8123, out language);
    }

    private IConfiguration CreateWebConfiguration(string runtime)
    {
        return new ConfigurationBuilder()
            .Add(HostConfigurationSnapshot.Parse(null, "web-function"))
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                [EnvironmentSettingNames.FunctionWorkerRuntime] = runtime
            })
            .Build();
    }

    private HttpWorkerOptionsSetup CreateSetup(IConfiguration configuration, bool standby = false)
    {
        Mock<IWorkerRuntimeResolver> runtimeResolver = new();
        runtimeResolver.Setup(resolver => resolver.GetWorkerRuntime(It.IsAny<string>())).Returns("custom");
        ScriptJobHostOptions hostOptions = new()
        {
            RootScriptPath = _app.Path,
            IsStandbyConfiguration = standby
        };

        return new HttpWorkerOptionsSetup(
            Options.Create(hostOptions), configuration, NullLoggerFactory.Instance, new TestMetricsLogger(), runtimeResolver.Object);
    }

    private void WriteFile(string path, string content = "")
    {
        string fullPath = Path.Combine(_app.Path, path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        File.WriteAllText(fullPath, content);
    }
}
