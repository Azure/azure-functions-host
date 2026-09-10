// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;
using Azure.Functions.Host.Controllers;
using Azure.Functions.Rpc.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Host;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Composition;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Azure.Functions.Host.Tests;

public class ClientWorkerCompositionTests
{
    private const string StartupCoordinatorTypeName = "Azure.Functions.Rpc.Client.RpcClientScriptHostStartupCoordinator";

    private static readonly string[] ForbiddenWorkerImplementations =
    [
        "Microsoft.Azure.WebJobs.Script.Grpc.AspNetCoreGrpcServer",
        "Microsoft.Azure.WebJobs.Script.Grpc.FunctionRpcService",
        "Microsoft.Azure.WebJobs.Script.Grpc.GrpcWorkerChannelFactory",
        "Microsoft.Azure.WebJobs.Script.Grpc.ServerDuplexChannelRegistry",
        "Microsoft.Azure.WebJobs.Script.Rpc.RpcScriptHostWorkerManager",
        "Microsoft.Azure.WebJobs.Script.Rpc.RpcWebHostWorkerManager",
        "Microsoft.Azure.WebJobs.Script.WorkerFunctionMetadataProvider",
        "Microsoft.Azure.WebJobs.Script.Workers.DefaultWorkerProcessFactory",
        "Microsoft.Azure.WebJobs.Script.Workers.FunctionInvocationDispatcherFactory",
        "Microsoft.Azure.WebJobs.Script.Workers.Http.DefaultHttpWorkerService",
        "Microsoft.Azure.WebJobs.Script.Workers.Http.HttpWorkerProcessFactory",
        "Microsoft.Azure.WebJobs.Script.Workers.HttpWorkerChannelFactory",
        "Microsoft.Azure.WebJobs.Script.Workers.EmptyProcessRegistry",
        "Microsoft.Azure.WebJobs.Script.Workers.JobObjectRegistry",
        "Microsoft.Azure.WebJobs.Script.Workers.Rpc.JobHostRpcWorkerChannelManager",
        "Microsoft.Azure.WebJobs.Script.Workers.Rpc.RpcFunctionInvocationDispatcherLoadBalancer",
        "Microsoft.Azure.WebJobs.Script.Workers.Rpc.RpcInitializationService",
        "Microsoft.Azure.WebJobs.Script.Workers.Rpc.RpcWorkerProcessFactory",
        "Microsoft.Azure.WebJobs.Script.Workers.Rpc.WebHostRpcWorkerChannelManager",
        "Microsoft.Azure.WebJobs.Script.Workers.WorkerConcurrencyManager",
    ];

    [Fact]
    public void ConfigureWebHostServices_RegistersExplicitRootClientGraph()
    {
        var services = new ServiceCollection();
        IMvcBuilder mvcBuilder = services.AddMvc();

        ClientWorkerComposition.Instance.ConfigureWebHostServices(services, mvcBuilder);

        AssertSingleton(services, "Microsoft.Azure.WebJobs.Script.Http.IHttpProxyService",
            "Microsoft.Azure.WebJobs.Script.Http.DefaultHttpProxyService");
        AssertSingleton(services, "Azure.Functions.Rpc.Client.IRpcClientFactory",
            "Azure.Functions.Rpc.Client.RpcClientFactory");
        AssertSingleton(services, "Azure.Functions.Rpc.Client.IDuplexChannelFactory`1",
            "Azure.Functions.Rpc.Client.FunctionRpcDuplexChannelFactory");
        AssertSingleton(services, "Azure.Functions.Rpc.Client.IRpcClientWorkerChannelFactory",
            "Azure.Functions.Rpc.Client.RpcClientWorkerChannelFactory");
        AssertSingleton(services, "Azure.Functions.Rpc.Client.IWorkerChannelRegistry",
            "Azure.Functions.Rpc.Client.WorkerChannelRegistry");
        AssertSingleton(services, "Microsoft.Azure.WebJobs.Script.IWorkerFunctionMetadataProvider",
            "Azure.Functions.Rpc.Client.RpcClientWorkerFunctionMetadataProvider");
        AssertSingleton(services, "Microsoft.Azure.WebJobs.Script.WebHost.IWebHostWorkerManager",
            "Azure.Functions.Host.ClientWebHostWorkerManager");
        AssertSingleton(services, "Microsoft.Azure.WebJobs.Script.IFunctionMetadataProvider",
            "Azure.Functions.Rpc.Client.RpcClientFunctionMetadataProvider");
        ServiceDescriptor coordinator = Assert.Single(services.Where(service =>
            string.Equals(service.ServiceType.FullName, StartupCoordinatorTypeName, StringComparison.Ordinal)));
        Assert.Equal(ServiceLifetime.Singleton, coordinator.Lifetime);
        Assert.NotNull(coordinator.ImplementationFactory);
        AssertNoServerWorkerServices(services);
    }

    [Fact]
    public void ConfigureWebHostServices_RegistersWorkerLinkControllerApplicationPart()
    {
        var services = new ServiceCollection();
        IMvcBuilder mvcBuilder = services.AddMvc();

        ClientWorkerComposition.Instance.ConfigureWebHostServices(services, mvcBuilder);

        ApplicationPartManager partManager = GetApplicationPartManager(services);
        var feature = new ControllerFeature();
        partManager.PopulateFeature(feature);

        Assert.Contains(feature.Controllers, controller => controller == typeof(WorkerLinkController).GetTypeInfo());
    }

    [Fact]
    public async Task ConfigureScriptHostServices_ResolvesChildGraphWithRootRegistryIdentity()
    {
        IHost rootHost = CreateRootHost(out IServiceCollection rootServices);
        try
        {
            Type registryType = GetServiceType(rootServices, "Azure.Functions.Rpc.Client.IWorkerChannelRegistry");
            object rootRegistry = rootHost.Services.GetRequiredService(registryType);
            IWorkerFunctionMetadataProvider rootMetadataProvider =
                rootHost.Services.GetRequiredService<IWorkerFunctionMetadataProvider>();
            IServiceCollection childServices = rootHost.Services.CreateChildContainer(rootServices);

            ClientWorkerComposition.Instance.ConfigureScriptHostServices(childServices, rootHost.Services);
            childServices.AddLogging();

            ServiceDescriptor[] registryDescriptors = childServices.Where(service => service.ServiceType == registryType).ToArray();
            Assert.Equal(2, registryDescriptors.Length);
            Assert.All(registryDescriptors, descriptor => Assert.Same(rootRegistry, descriptor.ImplementationInstance));
            ServiceDescriptor[] metadataDescriptors = childServices
                .Where(service => service.ServiceType == typeof(IWorkerFunctionMetadataProvider))
                .ToArray();
            Assert.Equal(2, metadataDescriptors.Length);
            Assert.Same(rootMetadataProvider, metadataDescriptors.Last().ImplementationInstance);
            AssertNoServerWorkerServices(childServices);
            Assert.DoesNotContain(childServices, descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                string.Equals(
                    GetImplementationTypeName(descriptor),
                    "Microsoft.Azure.WebJobs.Script.WebHost.WebJobsScriptHostService",
                    StringComparison.Ordinal));

            await using ServiceProvider childServiceProvider = childServices.BuildServiceProvider();
            Assert.Same(rootRegistry, childServiceProvider.GetRequiredService(registryType));
            Assert.All(childServiceProvider.GetServices(registryType), registry => Assert.Same(rootRegistry, registry));
            Assert.Same(rootMetadataProvider, childServiceProvider.GetRequiredService<IWorkerFunctionMetadataProvider>());
            Assert.All(childServiceProvider.GetServices<IWorkerFunctionMetadataProvider>(),
                metadataProvider => Assert.Same(rootMetadataProvider, metadataProvider));
            Assert.Equal("Azure.Functions.Rpc.Client.RpcClientFunctionInvocationDispatcher",
                childServiceProvider.GetRequiredService<IRpcClientFunctionInvocationDispatcher>().GetType().FullName);
            Assert.Equal("Azure.Functions.Rpc.Client.RpcClientFunctionInvocationDispatcherFactory",
                childServiceProvider.GetRequiredService<IFunctionInvocationDispatcherFactory>().GetType().FullName);
            Assert.Equal("Azure.Functions.Rpc.Client.RpcClientWorkerFunctionMetadataProvider",
                childServiceProvider.GetRequiredService<IWorkerFunctionMetadataProvider>().GetType().FullName);
            Assert.Equal("Microsoft.Azure.WebJobs.Script.Description.RpcWorkerFunctionDescriptorProviderFactory",
                childServiceProvider.GetRequiredService<IWorkerFunctionDescriptorProviderFactory>().GetType().FullName);
            Assert.Equal("Microsoft.Azure.WebJobs.Script.Rpc.RpcScriptHostLifecycleService",
                childServiceProvider.GetRequiredService<IScriptHostLifecycleService>().GetType().FullName);
            Assert.Equal("Azure.Functions.Rpc.Client.RpcClientScriptHostWorkerManager",
                childServiceProvider.GetRequiredService<IScriptHostWorkerManager>().GetType().FullName);
        }
        finally
        {
            await ((IAsyncDisposable)rootHost).DisposeAsync();
        }
    }

    [Fact]
    public void ConfigureServices_ValidatesArguments()
    {
        var services = new ServiceCollection();
        IMvcBuilder mvcBuilder = services.AddMvc();
        using ServiceProvider rootServiceProvider = new ServiceCollection().BuildServiceProvider();

        Assert.Throws<ArgumentNullException>(() =>
            ClientWorkerComposition.Instance.ConfigureWebHostServices(null!, mvcBuilder));
        Assert.Throws<ArgumentNullException>(() =>
            ClientWorkerComposition.Instance.ConfigureWebHostServices(services, null!));
        Assert.Throws<ArgumentNullException>(() =>
            ClientWorkerComposition.Instance.ConfigureScriptHostServices(null!, rootServiceProvider));
        Assert.Throws<ArgumentNullException>(() =>
            ClientWorkerComposition.Instance.ConfigureScriptHostServices(services, null!));
    }

    [Fact]
    public void StaticCompositionSelection_PreventsCombiningServerAndClientGraphs()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddWebJobsScriptHost(configuration);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => services.AddWebJobsScriptHost(configuration, ClientWorkerComposition.Instance));

        Assert.True(exception.Message.Contains("already been selected", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RootProviderConstruction_ResolvesClientRegistryAndControllerWithoutActivation()
    {
        IHost rootHost = CreateRootHost(out IServiceCollection services);
        try
        {
            Type registryType = GetServiceType(services, "Azure.Functions.Rpc.Client.IWorkerChannelRegistry");

            object registry = rootHost.Services.GetRequiredService(registryType);
            IWorkerFunctionMetadataProvider metadataProvider =
                rootHost.Services.GetRequiredService<IWorkerFunctionMetadataProvider>();
            IFunctionMetadataManager metadataManager = rootHost.Services.GetRequiredService<IFunctionMetadataManager>();
            IWebHostWorkerManager workerManager = rootHost.Services.GetRequiredService<IWebHostWorkerManager>();
            WorkerLinkController controller = ActivatorUtilities.CreateInstance<WorkerLinkController>(rootHost.Services);

            Assert.NotNull(controller);
            Assert.Equal("Azure.Functions.Rpc.Client.WorkerChannelRegistry", registry.GetType().FullName);
            Assert.Equal("Azure.Functions.Rpc.Client.RpcClientWorkerFunctionMetadataProvider",
                metadataProvider.GetType().FullName);
            Assert.IsType<FunctionMetadataManager>(metadataManager);
            Assert.IsType<ClientWebHostWorkerManager>(workerManager);
            Assert.Equal("Azure.Functions.Rpc.Client.RpcClientFunctionMetadataProvider",
                rootHost.Services.GetRequiredService<IFunctionMetadataProvider>().GetType().FullName);
            IHostedService[] hostedServices = rootHost.Services.GetServices<IHostedService>().ToArray();
            Assert.DoesNotContain(hostedServices, service => service is WebJobsScriptHostService);
            Type coordinatorType = GetServiceType(services, StartupCoordinatorTypeName);
            Assert.Same(rootHost.Services.GetRequiredService(coordinatorType),
                Assert.Single(hostedServices.Where(service => string.Equals(service.GetType().FullName, StartupCoordinatorTypeName, StringComparison.Ordinal))));
            Assert.Same(rootHost.Services.GetRequiredService<WebJobsScriptHostService>(),
                rootHost.Services.GetRequiredService<IScriptHostManager>());
            await workerManager.SpecializeAsync();
            await workerManager.WorkerWarmupAsync();
            AssertNoServerWorkerServices(services);
            Assert.DoesNotContain(services, descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                string.Equals(
                    GetImplementationTypeName(descriptor),
                    "Microsoft.Azure.WebJobs.Script.WebHost.WebJobsScriptHostService",
                    StringComparison.Ordinal));
        }
        finally
        {
            await ((IAsyncDisposable)rootHost).DisposeAsync();
        }
    }

    [Fact]
    public void StandardComposition_DoesNotRegisterClientStartupOrMetadata()
    {
        var services = new ServiceCollection();
        services.AddWebJobsScriptHost(new ConfigurationBuilder().Build());

        Assert.DoesNotContain(services, descriptor => string.Equals(descriptor.ServiceType.FullName, StartupCoordinatorTypeName, StringComparison.Ordinal));
        Assert.DoesNotContain(services, descriptor =>
            string.Equals(descriptor.ImplementationType?.FullName, "Azure.Functions.Rpc.Client.RpcClientFunctionMetadataProvider", StringComparison.Ordinal));
    }

    [Fact]
    public void FunctionsHostReferencesClientAndSharedWebHostWithoutDirectServer()
    {
        string[] references = typeof(ClientWorkerComposition).Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .OfType<string>()
            .ToArray();

        Assert.Contains("Azure.Functions.Rpc.Client", references);
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
        XElement[] projectReferences = project.Descendants("ProjectReference").ToArray();
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
        Assert.Equal(
            [
                "..\\Functions.Rpc.Client\\Functions.Rpc.Client.csproj",
                "..\\WebJobs.Script.WebHost\\WebJobs.Script.WebHost.csproj",
            ],
            projectReferences.Select(reference => reference.Attribute("Include")?.Value));
        Assert.DoesNotContain(projectReferences, reference =>
            reference.Attribute("Include")?.Value.Contains("Functions.Rpc.Server", StringComparison.Ordinal) is true);
        Assert.DoesNotContain(projectReferences, reference =>
            reference.Attribute("Include")?.Value.Contains("Functions.WorkerProxy", StringComparison.Ordinal) is true);
        Assert.True(string.Equals("Microsoft.AspNetCore.App", frameworkReference.Attribute("Include")?.Value, StringComparison.Ordinal));
        Assert.True(string.Equals("ComputeFilesToPublish", publishFilter.Attribute("AfterTargets")?.Value, StringComparison.Ordinal));
    }

    private static IHost CreateRootHost(out IServiceCollection services)
    {
        IServiceCollection capturedServices = null!;
        IConfiguration configuration = new ConfigurationBuilder().Build();
        IHost host = new HostBuilder()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.ConfigureServices(rootServices =>
                {
                    capturedServices = rootServices;
                    rootServices.AddSingleton(configuration);
                    rootServices.AddWebJobsScriptHost(configuration, ClientWorkerComposition.Instance);
                    rootServices.Configure<ScriptApplicationHostOptions>(options =>
                    {
                        options.LogPath = Path.GetTempPath();
                        options.ScriptPath = Directory.GetCurrentDirectory();
                        options.SecretsPath = Path.GetTempPath();
                    });
                });
                webBuilder.Configure(_ =>
                {
                });
            })
            .Build();

        services = capturedServices;
        return host;
    }

    private static Type GetServiceType(IServiceCollection services, string serviceTypeName)
    {
        return Assert.Single(services.Where(service =>
            string.Equals(GetTypeName(service.ServiceType), serviceTypeName, StringComparison.Ordinal))).ServiceType;
    }

    private static void AssertSingleton(
        IServiceCollection services,
        string serviceTypeName,
        string implementationTypeName)
    {
        ServiceDescriptor descriptor = Assert.Single(services.Where(service =>
            string.Equals(GetTypeName(service.ServiceType), serviceTypeName, StringComparison.Ordinal)));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.True(string.Equals(
            implementationTypeName,
            GetImplementationTypeName(descriptor),
            StringComparison.Ordinal));
    }

    private static void AssertNoServerWorkerServices(IEnumerable<ServiceDescriptor> services)
    {
        Assert.DoesNotContain(services, descriptor =>
            ForbiddenWorkerImplementations.Contains(descriptor.ServiceType.FullName, StringComparer.Ordinal) ||
            ForbiddenWorkerImplementations.Contains(descriptor.GetImplementationType()?.FullName, StringComparer.Ordinal));
    }

    private static string GetImplementationTypeName(ServiceDescriptor descriptor)
    {
        return descriptor.ImplementationType?.FullName
            ?? descriptor.ImplementationInstance?.GetType().FullName
            ?? string.Empty;
    }

    private static string GetTypeName(Type type)
        => (type.IsGenericType ? type.GetGenericTypeDefinition() : type).FullName ?? string.Empty;

    private static string GetProperty(XDocument project, string name)
        => project.Descendants(name).Select(element => element.Value).First();

    private static ApplicationPartManager GetApplicationPartManager(IServiceCollection services)
        => (ApplicationPartManager)services
            .Last(descriptor => descriptor.ServiceType == typeof(ApplicationPartManager))
            .ImplementationInstance!;
}
