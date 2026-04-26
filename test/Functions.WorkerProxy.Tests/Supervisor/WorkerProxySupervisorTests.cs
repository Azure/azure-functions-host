// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Functions.WorkerProxy.Supervisor;
using Xunit;

namespace Microsoft.Azure.Functions.WorkerProxy.Tests.Supervisor;

public class WorkerProxySupervisorTests
{
    [Fact]
    public async Task RunAsync_ReturnsZero_WhenWorkerProxyExitsCleanly()
    {
        var runner = new TestWorkerProxyProcessRunner(0);
        var supervisor = CreateSupervisor(runner, out StringWriter output, maxRestarts: 3);

        int exitCode = await supervisor.RunAsync(["--management-port", "8080"], CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, runner.RunCount);
        Assert.Equal(["--management-port", "8080"], runner.ArgumentsReceived);
        Assert.Contains("Starting WorkerProxy process. Attempt 1.", output.ToString());
    }

    [Fact]
    public async Task RunAsync_RestartsWorkerProxy_UntilCleanExit()
    {
        var runner = new TestWorkerProxyProcessRunner(42, 0);
        var supervisor = CreateSupervisor(runner, out StringWriter output, maxRestarts: 3);

        int exitCode = await supervisor.RunAsync([], CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(2, runner.RunCount);
        Assert.Contains("WorkerProxy exited with code 42; restarting attempt 1/3.", output.ToString());
        Assert.Contains("Starting WorkerProxy process. Attempt 2.", output.ToString());
    }

    [Fact]
    public async Task RunAsync_ReturnsLastExitCode_WhenRestartLimitReached()
    {
        var runner = new TestWorkerProxyProcessRunner(12, 13, 14);
        var supervisor = CreateSupervisor(runner, out StringWriter output, maxRestarts: 2);

        int exitCode = await supervisor.RunAsync([], CancellationToken.None);

        Assert.Equal(14, exitCode);
        Assert.Equal(3, runner.RunCount);
        Assert.Contains("WorkerProxy exited with code 12; restarting attempt 1/2.", output.ToString());
        Assert.Contains("WorkerProxy exited with code 13; restarting attempt 2/2.", output.ToString());
        Assert.Contains("WorkerProxy exited with code 14; restart limit 2 reached. Exiting.", output.ToString());
    }

    [Fact]
    public async Task RunAsync_TreatsRunnerExceptionAsFailedAttempt()
    {
        var runner = new TestWorkerProxyProcessRunner(new InvalidOperationException("start failed"), 0);
        var supervisor = CreateSupervisor(runner, out StringWriter output, maxRestarts: 1);

        int exitCode = await supervisor.RunAsync([], CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(2, runner.RunCount);
        Assert.Contains("WorkerProxy process failed to start or monitor", output.ToString());
        Assert.Contains("WorkerProxy exited with code 1; restarting attempt 1/1.", output.ToString());
    }

    [Fact]
    public async Task RunAsync_ReturnsShutdownExitCode_WhenCancellationRequested()
    {
        var cts = new CancellationTokenSource();
        var runner = new CancellingWorkerProxyProcessRunner(cts);
        var supervisor = CreateSupervisor(runner, out _, maxRestarts: 3);

        int exitCode = await supervisor.RunAsync([], cts.Token);

        Assert.Equal(WorkerProxySupervisor.ShutdownExitCode, exitCode);
        Assert.Equal(1, runner.RunCount);
    }

    private static WorkerProxySupervisor CreateSupervisor(
        IWorkerProxyProcessRunner runner,
        out StringWriter output,
        int maxRestarts)
    {
        output = new StringWriter();
        var logWriter = new FunctionLogWriter(
            output,
            new WorkerProxySupervisorLogContext("host-version", "container", "stamp", "tenant"),
            () => new DateTime(2026, 4, 25, 12, 34, 56, DateTimeKind.Utc),
            "123");

        return new WorkerProxySupervisor(
            new WorkerProxySupervisorOptions("workerproxy", maxRestarts, TimeSpan.Zero),
            runner,
            logWriter);
    }

    private sealed class TestWorkerProxyProcessRunner : IWorkerProxyProcessRunner
    {
        private readonly Queue<object> _results;

        public TestWorkerProxyProcessRunner(params object[] results)
        {
            _results = new Queue<object>(results);
        }

        public int RunCount { get; private set; }

        public IReadOnlyList<string>? ArgumentsReceived { get; private set; }

        public Task<int> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            Action<string> onOutputLine,
            CancellationToken cancellationToken)
        {
            RunCount++;
            ArgumentsReceived = arguments.ToArray();
            onOutputLine($"worker line {RunCount}");

            object result = _results.Count > 0 ? _results.Dequeue() : 0;
            if (result is Exception ex)
            {
                throw ex;
            }

            return Task.FromResult((int)result);
        }
    }

    private sealed class CancellingWorkerProxyProcessRunner : IWorkerProxyProcessRunner
    {
        private readonly CancellationTokenSource _cancellationTokenSource;

        public CancellingWorkerProxyProcessRunner(CancellationTokenSource cancellationTokenSource)
        {
            _cancellationTokenSource = cancellationTokenSource;
        }

        public int RunCount { get; private set; }

        public Task<int> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            Action<string> onOutputLine,
            CancellationToken cancellationToken)
        {
            RunCount++;
            _cancellationTokenSource.Cancel();
            return Task.FromResult(WorkerProxySupervisor.ShutdownExitCode);
        }
    }
}
