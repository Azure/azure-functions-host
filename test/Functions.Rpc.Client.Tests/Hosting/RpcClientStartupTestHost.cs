// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;

namespace Azure.Functions.Rpc.Client.Tests;

internal sealed class RpcClientStartupTestHost : IAsyncDisposable
{
    private readonly TaskCompletionSource<WorkerChannel> _firstLink = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public RpcClientStartupTestHost()
    {
        ScriptHostManager = ScriptHost.As<IScriptHostManager>();
        ScriptHostManager.SetupGet(manager => manager.State).Returns(ScriptHostState.Running);
        ScriptHost.Setup(host => host.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ScriptHost.Setup(host => host.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        Registry.Setup(registry => registry.DisposeAsync()).Returns(ValueTask.CompletedTask);
        Registry.Setup(registry => registry.WaitForFirstInitializedAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) =>
            {
                WaitEntered.TrySetResult();
                return _firstLink.Task.WaitAsync(token);
            });
        Logger.Setup(logger => logger.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        var loggerProvider = new Mock<ILoggerProvider>();
        loggerProvider.Setup(provider => provider.CreateLogger(It.IsAny<string>())).Returns(Logger.Object);

        Host = new HostBuilder().ConfigureServices(services =>
        {
            services.AddRpcClientWebHostServices(_ => ScriptHost.Object);
            services.AddLogging(logging => logging.AddProvider(loggerProvider.Object));
            services.Replace(ServiceDescriptor.Singleton<IWorkerChannelRegistry>(_ => Registry.Object));
        }).Build();
    }

    public Mock<IHostedService> ScriptHost { get; } = new(MockBehavior.Strict);

    public Mock<IScriptHostManager> ScriptHostManager { get; }

    public Mock<IWorkerChannelRegistry> Registry { get; } = new(MockBehavior.Strict);

    public Mock<ILogger> Logger { get; } = new();

    public TaskCompletionSource WaitEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IHost Host { get; }

    public RpcClientScriptHostStartupCoordinator Coordinator => Host.Services.GetRequiredService<RpcClientScriptHostStartupCoordinator>();

    public void CompleteLink() => _firstLink.TrySetResult(null!);

    public ValueTask DisposeAsync() => ((IAsyncDisposable)Host).DisposeAsync();
}
