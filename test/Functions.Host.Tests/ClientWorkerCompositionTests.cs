// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Azure.Functions.Host.Tests;

public class ClientWorkerCompositionTests
{
    [Fact]
    public async Task Program_SelectsClientWorkerComposition()
    {
        NotImplementedException exception = await Assert.ThrowsAsync<NotImplementedException>(() => Program.Main([]));

        Assert.True(exception.Message.Contains("Client WebHost worker composition", StringComparison.Ordinal));
    }

    [Fact]
    public void ConfigureWebHostServices_ThrowsUntilClientWorkerCompositionIsImplemented()
    {
        var services = new ServiceCollection();
        IMvcBuilder mvcBuilder = services.AddMvc();

        Assert.Throws<NotImplementedException>(
            () => ClientWorkerComposition.Instance.ConfigureWebHostServices(services, mvcBuilder));
    }

    [Fact]
    public void ConfigureScriptHostServices_ThrowsUntilClientWorkerCompositionIsImplemented()
    {
        var services = new ServiceCollection();
        using ServiceProvider rootServiceProvider = new ServiceCollection().BuildServiceProvider();

        Assert.Throws<NotImplementedException>(
            () => ClientWorkerComposition.Instance.ConfigureScriptHostServices(services, rootServiceProvider));
    }

    [Fact]
    public void FunctionsHostReferencesSharedWebHostEntryPointWithoutDirectServer()
    {
        string[] references = typeof(ClientWorkerComposition).Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .OfType<string>()
            .ToArray();

        Assert.Contains("Microsoft.Azure.WebJobs.Script", references);
        Assert.Contains("Microsoft.Azure.WebJobs.Script.WebHost", references);
        Assert.DoesNotContain("Azure.Functions.Rpc.Server", references);
        Assert.DoesNotContain("Azure.Functions.WorkerProxy", references);
    }

    [Fact]
    public void FunctionsHostProjectDefinesSharedWebHostEntrypointSettings()
    {
        XDocument project = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "ProjectFiles", "Functions.Host.csproj"));
        XElement root = project.Root ?? throw new InvalidDataException("Functions.Host.csproj is missing its root Project element.");
        XElement frameworkReference = Assert.Single(project.Descendants("FrameworkReference"));
        XElement projectReference = Assert.Single(project.Descendants("ProjectReference"));
        XElement publishFilter = Assert.Single(project.Descendants("Target")
            .Where(target => string.Equals(
                target.Attribute("Name")?.Value,
                "RemoveStandardWebHostEntrypointFromPublish",
                StringComparison.Ordinal)));

        Assert.True(string.Equals("Microsoft.NET.Sdk", root.Attribute("Sdk")?.Value, StringComparison.Ordinal));
        Assert.True(string.Equals("$(WorkersProps)", root.Elements("Import").Single().Attribute("Project")?.Value, StringComparison.Ordinal));
        Assert.True(string.Equals("linux-x64", GetProperty(project, "RuntimeIdentifiers"), StringComparison.Ordinal));
        Assert.True(string.Equals("true", GetProperty(project, "ServerGarbageCollection"), StringComparison.OrdinalIgnoreCase));
        Assert.True(string.Equals("false", GetProperty(project, "TieredCompilation"), StringComparison.OrdinalIgnoreCase));
        Assert.True(string.Equals(
            "$(MSBuildThisFileDirectory)..\\WebJobs.Script.WebHost\\runtimeconfig.template.json",
            GetProperty(project, "UserRuntimeConfig"),
            StringComparison.Ordinal));
        Assert.True(string.Equals(
            "..\\WebJobs.Script.WebHost\\WebJobs.Script.WebHost.csproj",
            projectReference.Attribute("Include")?.Value,
            StringComparison.Ordinal));
        Assert.True(string.Equals("Microsoft.AspNetCore.App", frameworkReference.Attribute("Include")?.Value, StringComparison.Ordinal));
        Assert.True(string.Equals("ComputeFilesToPublish", publishFilter.Attribute("AfterTargets")?.Value, StringComparison.Ordinal));
    }

    private static string GetProperty(XDocument project, string name)
        => project.Descendants(name).Select(element => element.Value).First();
}
