// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed class BlobListenerQueueGateTests
{
    /// <summary>
    /// Provides a visible command contract for exercising the production proxy without reflecting over test state.
    /// </summary>
    public interface ICommand
    {
        /// <summary>
        /// Runs the next queue-polling operation.
        /// </summary>
        Task<int> ExecuteAsync(CancellationToken cancellationToken);
    }

    internal interface IIsolatedCommand
    {
        Task<IsolatedResult> ExecuteAsync(CancellationToken cancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_PausesResumesAndCancelsWhileClosed()
    {
        int calls = 0;
        BlobListenerQueueGate gate = BlobListenerQueueGate.CreateProxy(typeof(ICommand));
        ICommand command = (ICommand)gate;
        gate.Initialize(_ => Task.FromResult(Interlocked.Increment(ref calls)), NullLogger.Instance);
        for (int cycle = 1; cycle <= 3; cycle++)
        {
            Task<int> pending = command.ExecuteAsync(CancellationToken.None);
            Assert.False(pending.IsCompleted);
            Assert.Equal(cycle - 1, calls);
            gate.SetEnabled(true);
            Assert.Equal(cycle, await pending.WaitAsync(TimeSpan.FromSeconds(10)));
            gate.SetEnabled(false);
        }

        using CancellationTokenSource cancellation = new();
        Task<int> canceled = command.ExecuteAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task ExecuteAsync_PauseAllowsAdmittedWorkToFinish()
    {
        TaskCompletionSource<int> finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
        BlobListenerQueueGate gate = BlobListenerQueueGate.CreateProxy(typeof(ICommand));
        ICommand command = (ICommand)gate;
        gate.Initialize(_ => finish.Task, NullLogger.Instance);
        gate.SetEnabled(true);
        Task<int> admitted = command.ExecuteAsync(CancellationToken.None);
        gate.SetEnabled(false);
        finish.SetResult(42);

        Assert.Equal(42, await admitted.WaitAsync(TimeSpan.FromSeconds(10)));
        using CancellationTokenSource cancellation = new();
        Task<int> next = command.ExecuteAsync(cancellation.Token);
        Assert.False(next.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next);
    }

    [Fact]
    public async Task ExecuteAsync_PreservesCommandExceptions()
    {
        InvalidOperationException failure = new("Command failure");
        BlobListenerQueueGate gate = BlobListenerQueueGate.CreateProxy(typeof(ICommand));
        ICommand command = (ICommand)gate;
        gate.Initialize<int>(_ => throw failure, NullLogger.Instance);
        gate.SetEnabled(true);

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteAsync(CancellationToken.None)));
    }

    [Fact]
    public async Task ExecuteAsync_SupportsNonPublicCommandInExtensionLoadContext()
    {
        BlobListenerQueueGate defaultGate = BlobListenerQueueGate.CreateProxy(typeof(ICommand));
        ICommand defaultCommand = (ICommand)defaultGate;
        defaultGate.Initialize(_ => Task.FromResult(42), NullLogger.Instance);
        Task<int> defaultPending = defaultCommand.ExecuteAsync(CancellationToken.None);
        Assert.False(defaultPending.IsCompleted);
        Func<Task>[] exercises = new Func<Task>[2];
        for (int i = 0; i < 2; i++)
        {
            FunctionAssemblyLoadContext context = new(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
            using Stream stream = File.OpenRead(typeof(BlobListenerQueueGateTests).Assembly.Location);
            Assembly isolated = context.LoadFromStream(stream);
            Type tests = isolated.GetType(typeof(BlobListenerQueueGateTests).FullName!, throwOnError: true)!;
            MethodInfo exercise = tests.GetMethod(nameof(CreateIsolatedCommandExercise))!;
            exercises[i] = (Func<Task>)exercise.Invoke(null, null)!;
        }

        defaultGate.SetEnabled(true);
        await Task.WhenAll(exercises[0](), exercises[1](), defaultPending).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(42, await defaultPending);
    }

    /// <summary>
    /// Creates a gate with an internal command and result from a separate extension load context.
    /// </summary>
    public static Func<Task> CreateIsolatedCommandExercise()
    {
        Assert.IsType<FunctionAssemblyLoadContext>(AssemblyLoadContext.GetLoadContext(typeof(BlobListenerQueueGateTests).Assembly));
        Assert.Same(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(typeof(BlobListenerQueueGate).Assembly));

        IsolatedCommandSource source = new();
        BlobListenerQueueGate gate = BlobListenerQueueGate.CreateProxy(typeof(IIsolatedCommand));
        IIsolatedCommand command = (IIsolatedCommand)gate;
        Assert.Same(AssemblyLoadContext.GetLoadContext(typeof(IIsolatedCommand).Assembly),
            AssemblyLoadContext.GetLoadContext(command.GetType().Assembly));
        MethodInfo execute = typeof(IIsolatedCommand).GetMethod(nameof(IIsolatedCommand.ExecuteAsync))!;
        gate.Initialize(execute.CreateDelegate<Func<CancellationToken, Task<IsolatedResult>>>(source), NullLogger.Instance);

        return async () =>
        {
            Task<IsolatedResult> pending = command.ExecuteAsync(CancellationToken.None);
            Assert.False(pending.IsCompleted);
            gate.SetEnabled(true);
            Assert.Equal(1, (await pending).Calls);
            gate.SetEnabled(false);

            using CancellationTokenSource cancellation = new();
            Task<IsolatedResult> canceled = command.ExecuteAsync(cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
            Assert.Equal(1, source.Calls);
        };
    }

    [Fact]
    public void Install_UnknownListener_ThrowsUnsupportedVersionWithCause()
    {
        IListener listener = Mock.Of<IListener>();

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => BlobListenerQueueGate.Install(listener, NullLogger.Instance));

        Assert.Contains(listener.GetType().Assembly.FullName, exception.Message, StringComparison.Ordinal);
        Assert.Contains("not supported", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(exception.InnerException);
    }

    internal readonly record struct IsolatedResult(int Calls);

    private sealed class IsolatedCommandSource : IIsolatedCommand
    {
        internal int Calls { get; private set; }

        public Task<IsolatedResult> ExecuteAsync(CancellationToken cancellationToken)
            => Task.FromResult(new IsolatedResult(++Calls));
    }
}
