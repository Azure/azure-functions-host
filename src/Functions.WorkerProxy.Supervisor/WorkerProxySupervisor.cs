// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.Functions.WorkerProxy.Supervisor;

internal sealed class WorkerProxySupervisor
{
    public const int ShutdownExitCode = 143;

    private readonly WorkerProxySupervisorOptions _options;
    private readonly IWorkerProxyProcessRunner _processRunner;
    private readonly FunctionLogWriter _logWriter;

    public WorkerProxySupervisor(
        WorkerProxySupervisorOptions options,
        IWorkerProxyProcessRunner processRunner,
        FunctionLogWriter logWriter)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _logWriter = logWriter ?? throw new ArgumentNullException(nameof(logWriter));
    }

    public async Task<int> RunAsync(IReadOnlyList<string> workerProxyArguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workerProxyArguments);

        int restartCount = 0;

        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ShutdownExitCode;
            }

            _logWriter.WriteSupervisorMessage(4, $"Starting WorkerProxy process. Attempt {restartCount + 1}.");

            int exitCode;
            try
            {
                exitCode = await _processRunner.RunAsync(
                    _options.WorkerProxyPath,
                    workerProxyArguments,
                    _logWriter.WriteProcessLine,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return ShutdownExitCode;
            }
            catch (Exception ex)
            {
                _logWriter.WriteSupervisorMessage(2, $"WorkerProxy process failed to start or monitor: {ex}");
                exitCode = 1;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return ShutdownExitCode;
            }

            if (exitCode == 0)
            {
                return 0;
            }

            if (restartCount >= _options.MaxRestarts)
            {
                _logWriter.WriteSupervisorMessage(
                    2,
                    $"WorkerProxy exited with code {exitCode}; restart limit {_options.MaxRestarts} reached. Exiting.");

                return exitCode;
            }

            restartCount++;
            _logWriter.WriteSupervisorMessage(
                2,
                $"WorkerProxy exited with code {exitCode}; restarting attempt {restartCount}/{_options.MaxRestarts}.");
        }
    }
}
