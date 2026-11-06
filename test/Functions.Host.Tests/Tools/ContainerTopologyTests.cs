// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.ComputeSeparation.AppHost;
using Xunit;

namespace Azure.Functions.Host.Tests.Tools;

public sealed class ContainerTopologyTests
{
    private static readonly string FixtureRoot = Path.Combine(AppContext.BaseDirectory, "ComputeSeparationFixtures");
    private static readonly string CertificatePath = Path.Combine(FixtureRoot, "server.crt");
    private static readonly string CertificateKeyPath = Path.Combine(FixtureRoot, "server.key");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupEnvironment_SuppliesEveryRequiredComposeInterpolation(bool worker)
    {
        await using ContainerTopology topology = CreateTopology();
        ComposeSession session = worker ? topology.WorkerPod : topology.Runtime;
        string yaml = await File.ReadAllTextAsync(session.ComposeFile);
        string[] required = Regex.Matches(yaml, @"\$\{(?<name>[A-Z0-9_]+):\?")
            .Select(match => match.Groups["name"].Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        Assert.NotEmpty(required);
        Assert.Equal(required, session.CleanupEnvironment.Keys.Order(StringComparer.Ordinal));
        foreach (string name in required)
        {
            Assert.True(session.CleanupEnvironment.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value),
                $"Cleanup requires a real value for {name}.");
        }
    }

    [Fact]
    public async Task CleanupEnvironment_RetainsTheRunsActualIdentitiesKeysAndCertificatePaths()
    {
        await using ContainerTopology topology = CreateTopology();
        IReadOnlyDictionary<string, string> runtime = topology.Runtime.CleanupEnvironment;
        IReadOnlyDictionary<string, string> worker = topology.WorkerPod.CleanupEnvironment;

        Assert.Equal(topology.NetworkName, runtime["COMPUTE_NETWORK_NAME"]);
        Assert.Equal(topology.NetworkName, worker["COMPUTE_NETWORK_NAME"]);
        Assert.Equal(topology.ProxyAlias, worker["WORKER_PROXY_ALIAS"]);
        Assert.Equal("runtime-pod", runtime["COMPUTE_RUNTIME_POD_NAME"]);
        Assert.Equal("worker-pod", worker["WORKER_POD_NAME"]);
        Assert.Equal("worker-id", worker["WORKER_ID"]);
        Assert.Equal("startup-request", worker["WORKER_REQUEST_ID"]);
        Assert.Equal("runtime-key", runtime["COMPUTE_RUNTIME_CONTAINER_KEY"]);
        Assert.Equal("worker-key", worker["WORKER_RUNTIME_ACCESS_KEY"]);
        Assert.Equal("app-key", runtime["COMPUTE_APP_AUTH_KEY"]);
        Assert.Equal(runtime["COMPUTE_APP_AUTH_KEY"], worker["WORKER_APP_AUTH_KEY"]);
        Assert.Equal(CertificatePath, worker["WORKER_PROXY_CERTIFICATE_PATH"]);
        Assert.Equal(CertificateKeyPath, worker["WORKER_PROXY_CERTIFICATE_KEY_PATH"]);
        Assert.NotEqual(topology.Runtime.ProjectName, topology.WorkerPod.ProjectName);
    }

    [Fact]
    public async Task Cleanup_RemovesWorkerBeforeRuntimeNetworkAndIgnoresOldGenerationStops()
    {
        List<(string Project, string Command)> commands = [];
        await using ContainerTopology topology = CreateTopology(startInfo =>
        {
            commands.Add((startInfo.ArgumentList[2], startInfo.ArgumentList[5]));
            Assert.Equal("docker", startInfo.FileName);
            Assert.True(startInfo.RedirectStandardOutput);
            Assert.True(startInfo.RedirectStandardError);
            Assert.False(startInfo.UseShellExecute);

            return Task.CompletedTask;
        });
        await topology.Runtime.BeforeStartAsync(CancellationToken.None);
        await topology.WorkerPod.BeforeStartAsync(CancellationToken.None);
        string generation = topology.WorkerPod.Generation;

        await topology.WorkerPod.StopRunAsync(generation);
        await topology.WorkerPod.BeforeStartAsync(CancellationToken.None);
        Assert.NotEqual(generation, topology.WorkerPod.Generation);
        await topology.WorkerPod.StopRunAsync(generation);
        Assert.Single(commands);
        await Task.WhenAll(topology.DisposeAsync().AsTask(), topology.DisposeAsync().AsTask());

        Assert.Equal(
        [
            (topology.WorkerPod.ProjectName, "down"),
            (topology.WorkerPod.ProjectName, "down"),
            (topology.Runtime.ProjectName, "stop"),
            (topology.Runtime.ProjectName, "down")
        ], commands);
    }

    [Fact]
    public async Task BeforeStart_AwaitsFailedCleanupAndDoesNotAdvanceGenerationUntilARealRetrySucceeds()
    {
        TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool fail = true;
        int commands = 0;
        await using ContainerTopology topology = CreateTopology(_ =>
        {
            commands++;

            return fail ? stopped.Task : Task.CompletedTask;
        });
        await topology.Runtime.BeforeStartAsync(CancellationToken.None);
        string generation = topology.Runtime.Generation;
        Task stop = topology.Runtime.StopRunAsync(generation);
        Task restart = topology.Runtime.BeforeStartAsync(CancellationToken.None);
        Assert.False(restart.IsCompleted);
        Assert.Equal(generation, topology.Runtime.Generation);
        stopped.SetException(new InvalidOperationException("controlled cleanup failure"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => stop);
        await Assert.ThrowsAsync<InvalidOperationException>(() => restart);
        Assert.Equal(1, commands);
        Assert.Equal(generation, topology.Runtime.Generation);
        fail = false;
        await topology.Runtime.BeforeStartAsync(CancellationToken.None);
        Assert.Equal(2, commands);
        Assert.NotEqual(generation, topology.Runtime.Generation);
    }

    [Fact]
    public async Task Cleanup_AttemptsRuntimeRemovalEvenWhenWorkerRemovalFails()
    {
        List<string> projects = [];
        ContainerTopology topology = CreateTopology(startInfo =>
        {
            string project = startInfo.ArgumentList[2];
            projects.Add(project);

            return project.EndsWith("-worker-1", StringComparison.Ordinal)
                ? Task.FromException(new InvalidOperationException("controlled worker cleanup failure")) : Task.CompletedTask;
        });
        await topology.Runtime.BeforeStartAsync(CancellationToken.None);
        await topology.WorkerPod.BeforeStartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => topology.DisposeAsync().AsTask());

        Assert.Equal([topology.WorkerPod.ProjectName, topology.Runtime.ProjectName, topology.Runtime.ProjectName], projects);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Constructor_DoesNotInventMissingCleanupInputs(int missingIndex)
    {
        string[] inputs =
        [
            "app-key", "runtime-key", "runtime-pod", "worker-id", "worker-pod", "worker-key",
            "startup-request", CertificatePath, CertificateKeyPath
        ];
        inputs[missingIndex] = string.Empty;

        Assert.Throws<ArgumentException>(() => new ContainerTopology(
            FixtureRoot, inputs[0], inputs[1], inputs[2], inputs[3], inputs[4], inputs[5], inputs[6], inputs[7], inputs[8]));
    }

    private static ContainerTopology CreateTopology(Func<ProcessStartInfo, Task>? runCommand = null)
        => new(FixtureRoot, "app-key", "runtime-key", "runtime-pod", "worker-id", "worker-pod", "worker-key",
            "startup-request", CertificatePath, CertificateKeyPath, runCommand ?? (_ => Task.CompletedTask));
}
