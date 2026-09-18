// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace Azure.Functions.ComputeSeparation.AppHost;

/// <summary>
/// Owns one Compose group's stop and removal lifecycle independently of other groups.
/// </summary>
internal sealed class ComposeSession(
    string repositoryRoot,
    string composeFile,
    string projectName,
    IReadOnlyDictionary<string, string> cleanupEnvironment,
    bool removeOnStop = false) : IAsyncDisposable
{
    public const string ProjectNameVariable = "COMPOSE_PROJECT_NAME";
    public const string GenerationVariable = "COMPOSE_RUN_GENERATION";

    private readonly Lock _lock = new();
    private Task? _stopTask = Task.CompletedTask;
    private Task? _disposeTask;
    private string _generation = Guid.NewGuid().ToString("N");

    public string ComposeFile { get; } = Path.Combine(repositoryRoot, "tools", "ComputeSeparation", composeFile);

    public string ProjectName { get; } = projectName;

    public string Generation
    {
        get
        {
            lock (_lock)
            {
                return _generation;
            }
        }
    }

    public async Task BeforeStartAsync(CancellationToken cancellationToken)
    {
        Task previousStop;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
            previousStop = StopAsync();
        }

        await previousStop.WaitAsync(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
            _generation = Guid.NewGuid().ToString("N");
            _stopTask = null;
        }
    }

    public Task StopRunAsync(string? generation)
    {
        lock (_lock)
        {
            // A stopped snapshot belongs to one run, even if a replacement has already started.
            if (!string.Equals(generation, _generation, StringComparison.Ordinal))
            {
                return Task.CompletedTask;
            }

            return StopAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            return new(_disposeTask ??= removeOnStop ? StopAsync() : CleanupAsync(StopAsync, RemoveAsync));
        }
    }

    public static async Task CleanupAsync(params Func<Task>[] actions)
    {
        List<Exception> failures = [];
        foreach (Func<Task> action in actions)
        {
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        else if (failures.Count > 1)
        {
            throw new AggregateException("Compose cleanup failed.", failures);
        }
    }

    private Task StopAsync()
    {
        lock (_lock)
        {
            if (_stopTask is null || _stopTask.IsFaulted || _stopTask.IsCanceled)
            {
                // The runtime retains its shared network until all worker pods have been removed.
                _stopTask = removeOnStop ? RemoveAsync() : RunAsync(["stop"]);
            }

            return _stopTask;
        }
    }

    private Task RemoveAsync() => RunAsync(["down", "--remove-orphans", "--rmi", "local"]);

    private async Task RunAsync(string[] arguments)
    {
        ProcessStartInfo startInfo = new("docker")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string argument in new[] { "compose", "--project-name", ProjectName, "--file", ComposeFile }.Concat(arguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Compose validates interpolation even for down; these values never create resources.
        foreach ((string key, string value) in cleanupEnvironment)
        {
            startInfo.Environment[key] = value;
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start Docker Compose cleanup.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(45));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync();
            throw new TimeoutException($"Docker Compose cleanup timed out for '{ProjectName}'. {await stderr}");
        }

        string output = await stdout;
        string error = await stderr;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Docker Compose cleanup failed for '{ProjectName}' ({process.ExitCode}). {error}{output}");
        }
    }
}
