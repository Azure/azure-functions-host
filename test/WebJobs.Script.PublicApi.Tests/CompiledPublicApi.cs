// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Script.PublicApi.Tests;

/// <summary>
/// Builds the compiled public API snapshot of every shipped assembly once per test process.
/// </summary>
internal static class CompiledPublicApi
{
    private static readonly Lazy<IReadOnlyList<CompiledAssembly>> LazyAssemblies =
        new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Gets every shipped assembly and its compiled public API snapshot, in manifest order.
    /// </summary>
    public static IReadOnlyList<CompiledAssembly> Assemblies => LazyAssemblies.Value;

    /// <summary>
    /// Gets the snapshot of a shipped assembly by simple name.
    /// </summary>
    /// <param name="assemblyName">The simple assembly name.</param>
    /// <returns>The snapshot.</returns>
    public static PublicApiSnapshot GetSnapshot(string assemblyName)
    {
        CompiledAssembly assembly = Assemblies
            .FirstOrDefault(candidate => string.Equals(candidate.Assembly.BaselineAssemblyName, assemblyName, StringComparison.Ordinal));

        return assembly?.Snapshot
            ?? throw new InvalidOperationException($"'{assemblyName}' is not a shipped assembly in the manifest.");
    }

    private static IReadOnlyList<CompiledAssembly> Build()
    {
        ShippedAssemblyManifest manifest = ShippedAssemblyManifest.Load();
        IReadOnlyList<string> probeDirectories = manifest.GetProbeDirectories();

        return manifest.Assemblies
            .Select(assembly => new CompiledAssembly(
                assembly,
                PublicApiSnapshotBuilder.Create(assembly.GetReleaseOutputPath(), probeDirectories)))
            .ToArray();
    }
}

/// <summary>
/// A shipped package entry and the compiled public API snapshot of the assembly it ships.
/// </summary>
internal sealed class CompiledAssembly
{
    public CompiledAssembly(ShippedAssemblyManifest.ShippedAssembly assembly, PublicApiSnapshot snapshot)
    {
        Assembly = assembly;
        Snapshot = snapshot;
    }

    /// <summary>
    /// Gets the shipped assembly manifest entry.
    /// </summary>
    public ShippedAssemblyManifest.ShippedAssembly Assembly { get; }

    /// <summary>
    /// Gets the compiled public API snapshot.
    /// </summary>
    public PublicApiSnapshot Snapshot { get; }
}
