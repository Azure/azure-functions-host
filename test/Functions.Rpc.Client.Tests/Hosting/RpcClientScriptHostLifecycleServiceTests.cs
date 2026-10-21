// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed class RpcClientScriptHostLifecycleServiceTests
{
    private readonly Mock<IRpcClientFunctionInvocationDispatcher> _dispatcher = new(MockBehavior.Strict);
    private readonly ManagedDependencyOptions _managedDependencyOptions = new();
    private readonly Mock<IWorkerChannelRegistry> _registry = new(MockBehavior.Strict);
    private readonly ScriptJobHostOptions _scriptHostOptions = new()
    {
        FunctionTimeout = TimeSpan.FromMinutes(5),
    };

    [Fact]
    public async Task InitializedAsync_EnrichesMetadataBeforeRegistryActivation()
    {
        FunctionMetadata function = CreateFunction();
        bool activated = false;
        _registry.Setup(registry => registry.ActivateAsync(
                It.IsAny<IReadOnlyList<FunctionMetadata>>(),
                _managedDependencyOptions,
                _scriptHostOptions.FunctionTimeout,
                It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<FunctionMetadata> functions, ManagedDependencyOptions _, TimeSpan? _, CancellationToken _) =>
            {
                FunctionMetadata activatedFunction = Assert.Single(functions);
                Assert.Equal(LogCategories.CreateFunctionUserCategory(function.Name),
                    activatedFunction.Properties[LogConstants.CategoryNameKey]);
                Assert.Equal(_scriptHostOptions.InstanceId,
                    activatedFunction.Properties[ScriptConstants.LogPropertyHostInstanceIdKey]);
                activated = true;
            })
            .Returns(Task.CompletedTask);
        _dispatcher.Setup(dispatcher => dispatcher.InitializeAsync(
                It.IsAny<IEnumerable<FunctionMetadata>>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => Assert.True(activated))
            .Returns(Task.CompletedTask);
        RpcClientScriptHostLifecycleService service = CreateService();

        await service.InitializedAsync([function], CancellationToken.None);

        _registry.VerifyAll();
        _dispatcher.VerifyAll();
    }

    [Fact]
    public async Task InitializedAsync_EmptyFunctionsStillActivatesRegistry()
    {
        _registry.Setup(registry => registry.ActivateAsync(
                It.Is<IReadOnlyList<FunctionMetadata>>(functions => functions.Count == 0),
                _managedDependencyOptions,
                _scriptHostOptions.FunctionTimeout,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _dispatcher.Setup(dispatcher => dispatcher.InitializeAsync(
                It.Is<IEnumerable<FunctionMetadata>>(functions => !System.Linq.Enumerable.Any(functions)),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        RpcClientScriptHostLifecycleService service = CreateService();

        await service.InitializedAsync([], CancellationToken.None);

        _registry.VerifyAll();
        _dispatcher.VerifyAll();
    }

    [Fact]
    public async Task InitializedAsync_InitializesDispatcherOnlyAfterActivation()
    {
        TaskCompletionSource activation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _registry.Setup(registry => registry.ActivateAsync(
                It.IsAny<IReadOnlyList<FunctionMetadata>>(),
                _managedDependencyOptions,
                _scriptHostOptions.FunctionTimeout,
                It.IsAny<CancellationToken>()))
            .Returns(activation.Task);
        _dispatcher.Setup(dispatcher => dispatcher.InitializeAsync(
                It.IsAny<IEnumerable<FunctionMetadata>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        RpcClientScriptHostLifecycleService service = CreateService();

        Task initialization = service.InitializedAsync([CreateFunction()], CancellationToken.None);
        _dispatcher.Verify(dispatcher => dispatcher.InitializeAsync(
            It.IsAny<IEnumerable<FunctionMetadata>>(),
            It.IsAny<CancellationToken>()), Times.Never);

        activation.SetResult();
        await initialization;

        _dispatcher.VerifyAll();
    }

    [Fact]
    public async Task InitializedAsync_ActivationFailurePropagatesWithoutInitializingDispatcher()
    {
        InvalidOperationException failure = new("setup failed");
        _registry.Setup(registry => registry.ActivateAsync(
                It.IsAny<IReadOnlyList<FunctionMetadata>>(),
                _managedDependencyOptions,
                _scriptHostOptions.FunctionTimeout,
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromException(failure));
        RpcClientScriptHostLifecycleService service = CreateService();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InitializedAsync([CreateFunction()], CancellationToken.None));

        Assert.Same(failure, exception);
        _dispatcher.Verify(dispatcher => dispatcher.InitializeAsync(
            It.IsAny<IEnumerable<FunctionMetadata>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StoppingAsync_PreShutsDownDispatcher()
    {
        _dispatcher.Setup(dispatcher => dispatcher.PreShutdown());
        RpcClientScriptHostLifecycleService service = CreateService();

        await service.StoppingAsync(CancellationToken.None);

        _dispatcher.VerifyAll();
    }

    private RpcClientScriptHostLifecycleService CreateService()
        => new(
            _registry.Object,
            _dispatcher.Object,
            Options.Create(_scriptHostOptions),
            Options.Create(_managedDependencyOptions));

    private static FunctionMetadata CreateFunction()
        => new()
        {
            Language = "external",
            Name = "TestFunction",
        };
}
