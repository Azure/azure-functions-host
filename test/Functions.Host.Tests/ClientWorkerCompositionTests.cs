// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;
using Azure.Functions.Authentication;
using Azure.Functions.Host.Controllers;
using Azure.Functions.Host.HostState;
using Azure.Functions.Host.WorkerLink;
using Azure.Functions.Rpc.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Host;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Composition;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Metrics;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
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

        Assert.DoesNotContain(services, service => service.ServiceType == typeof(IHttpProxyService));
        Assert.DoesNotContain(services, service => string.Equals(service.ServiceType.FullName,
            "Microsoft.Azure.WebJobs.Script.Http.DefaultHttpProxyService", StringComparison.Ordinal));
        ServiceDescriptor workerTransport = Assert.Single(services.Where(service =>
            service.ServiceType == typeof(Func<DelegatingHandler, IHttpProxyService>)));
        Assert.Equal(ServiceLifetime.Singleton, workerTransport.Lifetime);
        Assert.NotNull(workerTransport.ImplementationFactory);
        services.AddLogging();
        using (ServiceProvider provider = services.BuildServiceProvider())
        {
            AuthorizationPolicy managementPolicy = Assert.IsType<AuthorizationPolicy>(
                provider.GetRequiredService<IOptions<AuthorizationOptions>>()
                    .Value.GetPolicy(ContainerManagementAuthenticationHandler.SchemeName));
            Assert.Equal([ContainerManagementAuthenticationHandler.SchemeName], managementPolicy.AuthenticationSchemes);
            Func<DelegatingHandler, IHttpProxyService> factory = provider.GetRequiredService<Func<DelegatingHandler, IHttpProxyService>>();
            IHttpProxyService first = factory(Mock.Of<DelegatingHandler>());
            using IDisposable firstLifetime = Assert.IsAssignableFrom<IDisposable>(first);
            IHttpProxyService second = factory(Mock.Of<DelegatingHandler>());
            using IDisposable secondLifetime = Assert.IsAssignableFrom<IDisposable>(second);
            Assert.Equal("Microsoft.Azure.WebJobs.Script.Http.DefaultHttpProxyService", first.GetType().FullName);
            Assert.NotSame(first, second);
        }
        AssertSingleton(services, "Azure.Functions.Rpc.Client.IRpcClientFactory",
            "Azure.Functions.Rpc.Client.RpcClientFactory");
        AssertSingleton(services, "Azure.Functions.Rpc.Client.IDuplexChannelFactory`1",
            "Azure.Functions.Rpc.Client.FunctionRpcDuplexChannelFactory");
        AssertSingleton(services, "Azure.Functions.Rpc.Client.IRpcClientWorkerChannelFactory",
            "Azure.Functions.Rpc.Client.RpcClientWorkerChannelFactory");
        AssertSingleton(services, "Azure.Functions.Rpc.Client.WorkerChannelRegistry",
            "Azure.Functions.Rpc.Client.WorkerChannelRegistry");
        ServiceDescriptor registry = Assert.Single(services.Where(service => service.ServiceType == typeof(IWorkerChannelRegistry)));
        Assert.Equal(ServiceLifetime.Singleton, registry.Lifetime);
        Assert.NotNull(registry.ImplementationFactory);
        AssertSingleton(services, "Microsoft.Azure.WebJobs.Script.IWorkerFunctionMetadataProvider",
            "Azure.Functions.Rpc.Client.RpcClientWorkerFunctionMetadataProvider");
        AssertSingleton(services, "Microsoft.Azure.WebJobs.Script.WebHost.IWebHostWorkerManager",
            "Azure.Functions.Host.ClientWebHostWorkerManager");
        AssertSingleton(services, "Microsoft.Azure.WebJobs.Script.IFunctionMetadataProvider",
            "Azure.Functions.Rpc.Client.RpcClientFunctionMetadataProvider");
        AssertSingleton(services, "Azure.Functions.Host.HostState.IAppServerHostStateClient",
            "Azure.Functions.Host.HostState.AppServerHostStateClient");
        ServiceDescriptor stateManager = Assert.Single(services, service => service.ServiceType == typeof(IRuntimeStateManager));
        Assert.Equal(ServiceLifetime.Singleton, stateManager.Lifetime);
        Assert.NotNull(stateManager.ImplementationFactory);
        Assert.Single(services, service =>
            service.ServiceType == typeof(IHostedService) && service.ImplementationType == typeof(AppServerHostStatePublisher));
        ServiceDescriptor coordinator = Assert.Single(services.Where(service =>
            string.Equals(service.ServiceType.FullName, StartupCoordinatorTypeName, StringComparison.Ordinal)));
        Assert.Equal(ServiceLifetime.Singleton, coordinator.Lifetime);
        Assert.NotNull(coordinator.ImplementationFactory);
        AssertNoServerWorkerServices(services);
    }

    [Fact]
    public void ConfigureWebHostServices_UsesRootMetricsPublisherForWorkerAttribution()
    {
        var services = new ServiceCollection();
        IMvcBuilder mvcBuilder = services.AddMvc();
        var publisher = new Mock<IMetricsPublisher>();
        IWorkerInvocationTracker tracker = publisher.As<IWorkerInvocationTracker>().Object;
        services.AddSingleton(publisher.Object);

        ClientWorkerComposition.Instance.ConfigureWebHostServices(services, mvcBuilder);
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(tracker, provider.GetRequiredService<IWorkerInvocationTracker>());
        Assert.True(provider.GetRequiredService<IOptions<FlexConsumptionMetricsPublisherOptions>>().Value.UseWorkerAttribution);
    }

    [Fact]
    public void ConfigureWebHostServices_UsesSameComputeSeparatedPublisherForDispatchAndPublication()
    {
        var environment = new Mock<IEnvironment>();
        environment.Setup(value => value.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku))
            .Returns(ScriptConstants.FlexConsumptionSku);
        using IHost rootHost = CreateRootHost(out _, configureServices: services =>
        {
            services.AddSingleton(environment.Object);
            services.Configure<FlexConsumptionMetricsPublisherOptions>(options =>
                options.InitialPublishDelayMS = (int)TimeSpan.FromDays(1).TotalMilliseconds);
        });

        var publisher = Assert.IsType<FlexConsumptionPerWorkerMetricsPublisher>(rootHost.Services.GetRequiredService<IMetricsPublisher>());

        Assert.Same(publisher, rootHost.Services.GetRequiredService<IWorkerInvocationTracker>());
        Assert.Same(publisher, rootHost.Services.GetRequiredService<IMetricsPublisher>());
    }

    [Fact]
    public void ConfigureWebHostServices_UsesSystemTextJsonOnlyForWorkerLinkRequests()
    {
        ServiceCollection services = new();
        IMvcBuilder mvcBuilder = services.AddMvc().AddNewtonsoftJson();
        ClientWorkerComposition.Instance.ConfigureWebHostServices(services, mvcBuilder);
        using ServiceProvider provider = services.BuildServiceProvider();
        MvcOptions options = provider.GetRequiredService<IOptions<MvcOptions>>().Value;
        WorkerLinkRequestInputFormatter formatter = Assert.IsType<WorkerLinkRequestInputFormatter>(options.InputFormatters[0]);
        EmptyModelMetadataProvider metadataProvider = new();
        DefaultHttpContext context = new();
        context.Request.ContentType = "application/json";

        InputFormatterContext linkContext = new(context, "request", new ModelStateDictionary(),
            metadataProvider.GetMetadataForType(typeof(WorkerLinkRequest)), (stream, encoding) => new StreamReader(stream, encoding));
        InputFormatterContext unrelatedContext = new(context, "request", new ModelStateDictionary(),
            metadataProvider.GetMetadataForType(typeof(object)), (stream, encoding) => new StreamReader(stream, encoding));

        Assert.True(formatter.CanRead(linkContext));
        Assert.False(formatter.CanRead(unrelatedContext));
        Assert.Contains(options.InputFormatters, value => value is NewtonsoftJsonInputFormatter);
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
            IWorkerInvocationTracker rootInvocationTracker = rootHost.Services.GetRequiredService<IWorkerInvocationTracker>();
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
            Assert.Same(rootInvocationTracker, childServiceProvider.GetRequiredService<IWorkerInvocationTracker>());
            Assert.All(childServiceProvider.GetServices<IWorkerFunctionMetadataProvider>(),
                metadataProvider => Assert.Same(rootMetadataProvider, metadataProvider));
            Assert.Same(rootHost.Services.GetRequiredService<IRuntimeStateManager>(),
                childServiceProvider.GetRequiredService<IRuntimeStateManager>());
            Assert.Same(rootHost.Services.GetRequiredService<IAppServerHostStateClient>(),
                childServiceProvider.GetRequiredService<IAppServerHostStateClient>());
            Assert.Equal("Azure.Functions.Rpc.Client.RpcClientFunctionInvocationDispatcher",
                childServiceProvider.GetRequiredService<IRpcClientFunctionInvocationDispatcher>().GetType().FullName);
            Assert.Equal("Azure.Functions.Rpc.Client.RpcClientFunctionInvocationDispatcherFactory",
                childServiceProvider.GetRequiredService<IFunctionInvocationDispatcherFactory>().GetType().FullName);
            Assert.Equal("Azure.Functions.Rpc.Client.RpcClientWorkerFunctionMetadataProvider",
                childServiceProvider.GetRequiredService<IWorkerFunctionMetadataProvider>().GetType().FullName);
            Assert.Equal("Microsoft.Azure.WebJobs.Script.Description.RpcWorkerFunctionDescriptorProviderFactory",
                childServiceProvider.GetRequiredService<IWorkerFunctionDescriptorProviderFactory>().GetType().FullName);
            Assert.Equal("Azure.Functions.Rpc.Client.RpcClientScriptHostLifecycleService",
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
            IHostConfigurationSourceProvider configurationSource = rootHost.Services.GetRequiredService<IHostConfigurationSourceProvider>();
            IWorkerFunctionMetadataProvider metadataProvider =
                rootHost.Services.GetRequiredService<IWorkerFunctionMetadataProvider>();
            IFunctionMetadataManager metadataManager = rootHost.Services.GetRequiredService<IFunctionMetadataManager>();
            IWebHostWorkerManager workerManager = rootHost.Services.GetRequiredService<IWebHostWorkerManager>();
            WorkerLinkController controller = ActivatorUtilities.CreateInstance<WorkerLinkController>(rootHost.Services);

            Assert.NotNull(controller);
            Assert.Same(registry, configurationSource);
            Assert.Throws<InvalidOperationException>(() => configurationSource.GetConfigurationSource());
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
            IRuntimeStateManager stateManager = rootHost.Services.GetRequiredService<IRuntimeStateManager>();
            int stateManagerIndex = Array.FindIndex(hostedServices, service => ReferenceEquals(service, stateManager));
            int publisherIndex = Array.FindIndex(hostedServices, service => service is AppServerHostStatePublisher);
            Assert.Single(hostedServices, service => ReferenceEquals(service, stateManager));
            Assert.Single(hostedServices, service => service is AppServerHostStatePublisher);
            Assert.True(stateManagerIndex < publisherIndex);
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
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IHostConfigurationSourceProvider));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IRuntimeStateManager));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IAppServerHostStateClient));
        Assert.DoesNotContain(services, descriptor => descriptor.ImplementationType == typeof(AppServerHostStatePublisher));
    }

    [Fact]
    public void StandardComposition_SupportsPlaceholderScriptHost()
    {
        var services = new ServiceCollection();
        services.AddWebJobsScriptHost(new ConfigurationBuilder().Build());
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.True(provider.GetRequiredService<IOptions<StandbyOptions>>().Value.SupportsPlaceholderScriptHost);
    }

    [Fact]
    public void ConfigureWebHostServices_DisablesPlaceholderScriptHostSupport()
    {
        var services = new ServiceCollection();
        services.AddWebJobsScriptHost(new ConfigurationBuilder().Build(), ClientWorkerComposition.Instance);
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.False(provider.GetRequiredService<IOptions<StandbyOptions>>().Value.SupportsPlaceholderScriptHost);
    }

    [Fact]
    public async Task ScriptHostBuilds_DoNotRegisterBackgroundSync_AndPreserveExplicitSync()
    {
        var environment = new Mock<IEnvironment>();
        environment.Setup(value => value.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey))
            .Returns("test-key");
        var sourceProvider = new Mock<IHostConfigurationSourceProvider>();
        sourceProvider.Setup(value => value.GetConfigurationSource())
            .Returns(HostConfigurationSnapshot.Parse("""{"version":"2.0"}"""));

        IHost rootHost = CreateRootHost(out IServiceCollection rootServices, configureServices: services =>
        {
            services.AddSingleton(environment.Object);
            services.AddSingleton(sourceProvider.Object);
        });
        try
        {
            var webHostEnvironment = rootHost.Services.GetRequiredService<IScriptWebHostEnvironment>();
            Assert.False(webHostEnvironment.InStandbyMode);
            Assert.IsType<FunctionsSyncManager>(rootHost.Services.GetRequiredService<IFunctionsSyncManager>());

            for (int build = 0; build < 2; build++)
            {
                using IHost scriptHost = new HostBuilder()
                    .AddWebScriptHost(
                        rootHost.Services,
                        rootServices,
                        rootHost.Services.GetRequiredService<IOptions<ScriptApplicationHostOptions>>().Value)
                    .ConfigureServices(services =>
                    {
                        Assert.DoesNotContain(services, descriptor =>
                            descriptor.ServiceType == typeof(IHostedService) &&
                            descriptor.ImplementationType == typeof(FunctionsSyncService));
                    })
                    .Build();
            }
        }
        finally
        {
            await ((IAsyncDisposable)rootHost).DisposeAsync();
        }
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

    private static IHost CreateRootHost(out IServiceCollection services, IConfiguration? rootConfiguration = null,
        Action<IServiceCollection>? configureServices = null)
    {
        IServiceCollection capturedServices = null!;
        IConfiguration configuration = rootConfiguration ?? new ConfigurationBuilder().Build();
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
                    configureServices?.Invoke(rootServices);
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
