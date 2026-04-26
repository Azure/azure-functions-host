// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;

namespace Microsoft.Azure.Functions.WorkerProxy.Supervisor;

internal sealed class ProcessWorkerProxyRunner : IWorkerProxyProcessRunner
{
    private readonly TimeSpan _shutdownGracePeriod;

    public ProcessWorkerProxyRunner(TimeSpan shutdownGracePeriod)
    {
        _shutdownGracePeriod = shutdownGracePeriod;
    }

    public async Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        Action<string> onOutputLine,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(onOutputLine);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start WorkerProxy process '{fileName}'.");
        }

        Task stdoutTask = ReadLinesAsync(process.StandardOutput, onOutputLine);
        Task stderrTask = ReadLinesAsync(process.StandardError, onOutputLine);
        Task waitTask = process.WaitForExitAsync(CancellationToken.None);

        if (cancellationToken.CanBeCanceled && !waitTask.IsCompleted)
        {
            var cancellationTaskSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellationRegistration = cancellationToken.Register(
                static state => ((TaskCompletionSource)state!).TrySetResult(),
                cancellationTaskSource);

            Task completed = await Task.WhenAny(waitTask, cancellationTaskSource.Task);
            if (completed == cancellationTaskSource.Task && !process.HasExited)
            {
                ProcessTerminator.RequestGracefulTermination(process);

                Task gracePeriodTask = Task.Delay(_shutdownGracePeriod, CancellationToken.None);
                if (await Task.WhenAny(waitTask, gracePeriodTask) != waitTask && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
        }

        await waitTask;
        await Task.WhenAll(stdoutTask, stderrTask);

        return cancellationToken.IsCancellationRequested ? WorkerProxySupervisor.ShutdownExitCode : process.ExitCode;
    }

    private static async Task ReadLinesAsync(TextReader reader, Action<string> onOutputLine)
    {
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            onOutputLine(line);
        }
    }
}
