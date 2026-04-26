// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Runtime.InteropServices;

namespace Microsoft.Azure.Functions.WorkerProxy.Supervisor;

internal static class SupervisorEntryPoint
{
    public static async Task<int> RunAsync(string[] args)
    {
        var logWriter = FunctionLogWriter.CreateFromEnvironment(Console.Out);

        try
        {
            using var shutdown = new CancellationTokenSource();
            using var registrations = new CompositeDisposable(RegisterShutdownSignals(shutdown));

            var options = WorkerProxySupervisorOptions.Default;
            var processRunner = new ProcessWorkerProxyRunner(options.ShutdownGracePeriod);
            var supervisor = new WorkerProxySupervisor(options, processRunner, logWriter);

            return await supervisor.RunAsync(args, shutdown.Token);
        }
        catch (Exception ex)
        {
            logWriter.WriteSupervisorMessage(1, $"WorkerProxy supervisor failed unexpectedly: {ex}");
            return 1;
        }
    }

    private static IReadOnlyList<IDisposable> RegisterShutdownSignals(CancellationTokenSource shutdown)
    {
        var registrations = new List<IDisposable>();

        try
        {
            registrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                shutdown.Cancel();
            }));

            registrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
            {
                context.Cancel = true;
                shutdown.Cancel();
            }));
        }
        catch (PlatformNotSupportedException)
        {
        }

        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        Console.CancelKeyPress += cancelHandler;
        registrations.Add(new CallbackDisposable(() => Console.CancelKeyPress -= cancelHandler));

        return registrations;
    }

    private sealed class CallbackDisposable : IDisposable
    {
        private readonly Action _dispose;

        public CallbackDisposable(Action dispose)
        {
            _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
        }

        public void Dispose() => _dispose();
    }

    private sealed class CompositeDisposable : IDisposable
    {
        private readonly IReadOnlyList<IDisposable> _disposables;

        public CompositeDisposable(IReadOnlyList<IDisposable> disposables)
        {
            _disposables = disposables;
        }

        public void Dispose()
        {
            foreach (IDisposable disposable in _disposables)
            {
                disposable.Dispose();
            }
        }
    }
}
