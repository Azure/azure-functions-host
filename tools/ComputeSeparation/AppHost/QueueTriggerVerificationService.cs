// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aspire.Hosting.ApplicationModel;
using Azure.Storage.Queues;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.ComputeSeparation.AppHost;

/// <summary>
/// Verifies a real queue input and Host-managed output binding, then stops this acceptance run.
/// </summary>
internal sealed partial class QueueTriggerVerificationService(
    ReferenceExpression storageConnection,
    string inputQueue,
    string outputQueue,
    ContainerTopology topology,
    EndpointReference hostEndpoint,
    string evidenceDirectory,
    HostLinkService link,
    IHostApplicationLifetime lifetime,
    ILogger<QueueTriggerVerificationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(20));
        try
        {
            await link.Linked.WaitAsync(deadline.Token);
            string connectionString = await storageConnection.GetValueAsync(deadline.Token)
                ?? throw new InvalidOperationException("The emulator storage connection was not allocated.");
            QueueClientOptions options = new() { MessageEncoding = QueueMessageEncoding.Base64 };
            QueueClient input = new(connectionString, inputQueue, options);
            QueueClient output = new(connectionString, outputQueue, options);
            await input.CreateIfNotExistsAsync(cancellationToken: deadline.Token);
            await output.CreateIfNotExistsAsync(cancellationToken: deadline.Token);
            string token = $"extension-payload-{Guid.NewGuid():N}";
            await input.SendMessageAsync(token, cancellationToken: deadline.Token);
            Log.InputSent(logger, token);

            while (true)
            {
                var received = await output.ReceiveMessageAsync(cancellationToken: deadline.Token);
                if (received.Value is not null)
                {
                    if (!string.Equals(received.Value.MessageText, token, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("The QueueTrigger output did not match this run's token.");
                    }

                    await output.DeleteMessageAsync(received.Value.MessageId, received.Value.PopReceipt, deadline.Token);
                    await VerifyHttpAsync(deadline.Token);
                    await CaptureEvidenceAsync(token, deadline.Token);
                    Log.InvocationVerified(logger, token);
                    lifetime.StopApplication();
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250), deadline.Token);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            Environment.ExitCode = 1;
            Log.VerificationCanceled(logger);
        }
        catch (Exception exception)
        {
            Environment.ExitCode = 1;
            Log.VerificationFailed(logger, exception);
            lifetime.StopApplication();
        }
    }

    private async Task VerifyHttpAsync(CancellationToken cancellationToken)
    {
        string hostAddress = await hostEndpoint.GetValueAsync(cancellationToken)
            ?? throw new InvalidOperationException("The Host HTTP endpoint was not allocated.");
        using HttpClient client = new();
        string response = await client.GetStringAsync(new Uri(new Uri(hostAddress), "/api/hello"), cancellationToken);
        if (!response.StartsWith("Hello from the BYOC .NET isolated worker. Worker ID: ", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The shared sample did not return its expected HTTP response.");
        }
    }

    private async Task CaptureEvidenceAsync(string token, CancellationToken cancellationToken)
    {
        string runtime = $"{topology.Runtime.ProjectName}-functions-host-1";
        string proxy = $"{topology.WorkerPod.ProjectName}-worker-proxy-1";
        string worker = $"{topology.WorkerPod.ProjectName}-isolated-worker-1";
        string runtimeLogs;
        string invocationId;
        while (true)
        {
            runtimeLogs = await ReadDockerAsync(["logs", runtime], cancellationToken);
            Match invocation = Regex.Match(runtimeLogs, @"Executed 'Functions\.QueueEcho' \(Succeeded, Id=([0-9a-fA-F-]+)");
            invocationId = invocation.Success ? invocation.Groups[1].Value : string.Empty;
            int received = runtimeLogs.IndexOf("Received complete extension payload", StringComparison.Ordinal);
            int loaded = runtimeLogs.IndexOf("Loaded extension 'AzureStorageQueues'", StringComparison.Ordinal);
            int starting = runtimeLogs.IndexOf("Starting JobHost", StringComparison.Ordinal);
            if (Guid.TryParse(invocationId, out _) &&
                runtimeLogs.Contains("/active/.azurefunctions'", StringComparison.Ordinal) &&
                received >= 0 && loaded > received && starting > loaded && invocation.Index > starting)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        await ReadDockerAsync(["exec", worker, "sh", "-c",
            "test -f /home/site/wwwroot/extensions.json && test -f /home/site/wwwroot/.azurefunctions/Microsoft.Azure.WebJobs.Extensions.FunctionMetadataLoader.dll"], cancellationToken);
        await ReadDockerAsync(["exec", runtime, "sh", "-c",
            "test ! -e /home/site/wwwroot/extensions.json && test ! -e /app-content/extensions.json && test ! -d /app-content/.azurefunctions"], cancellationToken);
        await ReadDockerAsync(["exec", proxy, "sh", "-c",
            "test ! -e /home/site/wwwroot/extensions.json && test ! -e /app/extensions.json && test ! -d /app/.azurefunctions"], cancellationToken);

        Match payload = Regex.Match(runtimeLogs,
            @"Script Startup resetting load context with base path: '(?<root>/[^'\r\n]+/active)/\.azurefunctions'\.");
        if (!payload.Success)
        {
            throw new InvalidOperationException("Could not locate the validated extension payload in the Host logs.");
        }

        string payloadRoot = payload.Groups["root"].Value;
        await ReadDockerAsync(["exec", runtime, "sh", "-c",
            "test -f \"$1/.azurefunctions/Microsoft.Azure.WebJobs.Extensions.Storage.Queues.dll\" && test ! -e \"$1/.azurefunctions/Microsoft.Azure.WebJobs.Extensions.FunctionMetadataLoader.dll\" && test ! -e \"$1/.azurefunctions/Microsoft.Azure.WebJobs.Extensions.FunctionMetadataLoader.pdb\"",
            "verify-payload", payloadRoot], cancellationToken);
        string workerManifest = await ReadDockerAsync(["exec", worker, "cat", "/home/site/wwwroot/extensions.json"], cancellationToken);
        string payloadManifest = await ReadDockerAsync(["exec", runtime, "cat", $"{payloadRoot}/extensions.json"], cancellationToken);
        const string BootstrapStartup = "Microsoft.Azure.WebJobs.Extensions.FunctionMetadataLoader.Startup";
        if (!workerManifest.Contains(BootstrapStartup, StringComparison.Ordinal) || payloadManifest.Contains(BootstrapStartup, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The bootstrap must remain in the worker deployment and be filtered only from the transferred manifest.");
        }

        Directory.CreateDirectory(evidenceDirectory);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "worker-extensions.json"), workerManifest, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "payload-extensions.json"), payloadManifest, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "host.log"), runtimeLogs, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "proxy.log"),
            await ReadDockerAsync(["logs", proxy], cancellationToken), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "worker.log"),
            await ReadDockerAsync(["logs", worker], cancellationToken), cancellationToken);
        string[] inspectArguments = ["inspect", "--format", """{"container":{{json .Name}},"mounts":{{json .Mounts}}}""", runtime, proxy, worker];
        string mounts = await ReadDockerAsync(inspectArguments, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "mounts.jsonl"), mounts, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "result.json"), JsonSerializer.Serialize(new
        {
            status = "passed",
            token,
            invocationId,
            inputQueue,
            outputQueue,
            httpFunctionAvailable = true,
            streamedQueueExtensionLoaded = true,
            extensionLoadedBeforeJobHostStarted = true,
            deploymentAvailableOnlyInWorker = true,
            metadataBootstrapRetainedOnWorker = true,
            metadataBootstrapFilteredFromPayload = true,
            runtime,
            proxy,
            worker,
        }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        Log.EvidenceCaptured(logger, evidenceDirectory);
    }

    private static async Task<string> ReadDockerAsync(string[] arguments, CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start Docker evidence collection.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        string text = await output + await error;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Docker evidence collection failed ({process.ExitCode}): {text}");
        }

        return text;
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Information, "QueueTrigger acceptance enqueued token {Token}.")]
        public static partial void InputSent(ILogger logger, string token);

        [LoggerMessage(1, LogLevel.Information, "QueueTrigger acceptance succeeded: Host -> WorkerProxy -> worker -> Host output binding, token {Token}.")]
        public static partial void InvocationVerified(ILogger logger, string token);

        [LoggerMessage(2, LogLevel.Error, "QueueTrigger acceptance failed.")]
        public static partial void VerificationFailed(ILogger logger, Exception exception);

        [LoggerMessage(3, LogLevel.Warning, "QueueTrigger acceptance stopped before a correlated output was verified.")]
        public static partial void VerificationCanceled(ILogger logger);

        [LoggerMessage(4, LogLevel.Information, "QueueTrigger acceptance evidence saved at {EvidenceDirectory}.")]
        public static partial void EvidenceCaptured(ILogger logger, string evidenceDirectory);
    }
}
