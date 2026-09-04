// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Workers;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public class RpcClientAssemblyTests
{
    [Fact]
    public void GeneratedClientAndServerAreOwnedByGrpc()
    {
        Assert.Equal("Microsoft.Azure.WebJobs.Script.Grpc", typeof(FunctionRpc.FunctionRpcClient).Assembly.GetName().Name);
        Assert.Same(typeof(FunctionRpc.FunctionRpcClient).Assembly, typeof(FunctionRpc.FunctionRpcBase).Assembly);
    }

    [Fact]
    public void ClientReferencesGrpcAndDoesNotReferenceServer()
    {
        string[] references = typeof(GrpcDuplexChannel<StreamingMessage>).Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToArray();

        Assert.Contains("Microsoft.Azure.WebJobs.Script.Grpc", references);
        Assert.DoesNotContain("Azure.Functions.Rpc.Server", references);
        Assert.DoesNotContain("Azure.Functions.WorkerProxy", references);
    }

    [Fact]
    public void GrpcDoesNotReferenceClient()
    {
        string[] references = typeof(FunctionRpc).Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToArray();

        Assert.DoesNotContain("Azure.Functions.Rpc.Client", references);
    }

    [Fact]
    public void RpcClientWorkerChannelDerivesPublicSharedWorkerChannel()
    {
        Assert.True(typeof(WorkerChannel).IsPublic);
        Assert.Equal(typeof(WorkerChannel), typeof(RpcClientWorkerChannel).BaseType);
        Assert.Equal("Azure.Functions.Rpc.Client", typeof(RpcClientWorkerChannel).Assembly.GetName().Name);
    }

    [Fact]
    public void ClientDispatcherUsesPublicSharedContracts()
    {
        Assert.True(typeof(IFunctionInvocationDispatcher).IsPublic);
        Assert.True(typeof(IFunctionInvocationDispatcherFactory).IsPublic);
        Assert.True(typeof(FunctionInvocationDispatcherState).IsPublic);
        Assert.True(typeof(IRpcClientFunctionInvocationDispatcher).IsPublic);
        Assert.Contains(typeof(IFunctionInvocationDispatcher),
            typeof(RpcClientFunctionInvocationDispatcher).GetInterfaces());
        Assert.Contains(typeof(IRpcClientFunctionInvocationDispatcher),
            typeof(RpcClientFunctionInvocationDispatcher).GetInterfaces());
    }

    [Fact]
    public void ClientMetadataProviderUsesPublicSharedContracts()
    {
        Assert.True(typeof(IWorkerFunctionMetadataProvider).IsPublic);
        Assert.True(typeof(FunctionMetadataResult).IsPublic);
        Assert.True(typeof(WorkerFunctionMetadataValidator).IsPublic);
        Assert.Contains(typeof(IWorkerFunctionMetadataProvider),
            typeof(RpcClientWorkerFunctionMetadataProvider).GetInterfaces());
    }

    [Fact]
    public void ProductProjectReferencesPreserveSiblingBoundaries()
    {
        string[] clientReferences = GetProjectReferences("Functions.Rpc.Client.csproj");
        string[] expectedClientReferences = ["..\\WebJobs.Script.Grpc\\WebJobs.Script.Grpc.csproj", "..\\WebJobs.Script\\WebJobs.Script.csproj"];
        Assert.Equal(expectedClientReferences.OrderBy(reference => reference, StringComparer.Ordinal),
            clientReferences.OrderBy(reference => reference, StringComparer.Ordinal));
        Assert.Equal(["..\\WebJobs.Script\\WebJobs.Script.csproj"], GetProjectReferences("WebJobs.Script.Grpc.csproj"));
        Assert.DoesNotContain(GetProjectReferences("Functions.Rpc.Server.csproj"),
            reference => reference.Contains("Functions.Rpc.Client", StringComparison.Ordinal));
        string[] webHostReferences = GetProjectReferences("WebJobs.Script.WebHost.csproj");
        Assert.Contains("..\\Functions.Rpc.Server\\Functions.Rpc.Server.csproj", webHostReferences);
        Assert.DoesNotContain(webHostReferences, reference => reference.Contains("Functions.Rpc.Client", StringComparison.Ordinal));
        Assert.DoesNotContain(webHostReferences, reference => reference.Contains("Functions.WorkerProxy", StringComparison.Ordinal));
    }

    [Fact]
    public void StandardSolutionDoesNotContainClient()
    {
        string solution = File.ReadAllText(GetProjectFilePath("WebJobs.Script.sln"));
        Assert.DoesNotContain("Functions.Host", solution, StringComparison.Ordinal);
        Assert.DoesNotContain("Functions.Rpc.Client", solution, StringComparison.Ordinal);
        Assert.DoesNotContain("Functions.WorkerProxy", solution, StringComparison.Ordinal);
    }

    [Fact]
    public void HostSolutionContainsClientAndSharedDependencies()
    {
        XDocument solution = XDocument.Load(GetProjectFilePath("Azure.Functions.Host.slnx"));
        string[] projects = solution.Descendants("Project")
            .Select(project => project.Attribute("Path")?.Value)
            .Where(path => path is not null)
            .ToArray();

        Assert.Contains("src/Functions.Rpc.Client/Functions.Rpc.Client.csproj", projects);
        Assert.Contains("src/Functions.Rpc.Server/Functions.Rpc.Server.csproj", projects);
        Assert.Contains("src/Functions.Host/Functions.Host.csproj", projects);
        Assert.Contains("src/WebJobs.Script/WebJobs.Script.csproj", projects);
        Assert.Contains("src/WebJobs.Script.Grpc/WebJobs.Script.Grpc.csproj", projects);
        Assert.Contains("src/WebJobs.Script.WebHost/WebJobs.Script.WebHost.csproj", projects);
        Assert.Contains("test/Functions.Host.Tests/Functions.Host.Tests.csproj", projects);
        Assert.Contains("test/Functions.Rpc.Client.Tests/Functions.Rpc.Client.Tests.csproj", projects);
    }

    private static string[] GetProjectReferences(string projectFile)
    {
        XDocument project = XDocument.Load(GetProjectFilePath(projectFile));
        return project.Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(reference => reference is not null)
            .ToArray();
    }

    private static string GetProjectFilePath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "ProjectFiles", fileName);
}
