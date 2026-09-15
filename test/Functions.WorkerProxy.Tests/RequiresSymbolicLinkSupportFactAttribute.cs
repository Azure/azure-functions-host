// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

/// <summary>
/// Marks a test that cannot run without the privilege symbolic link creation requires, and
/// reports it as skipped where that privilege is missing. A test that returns early instead
/// would report a pass for coverage it never exercised, which is how a deleted guard reaches
/// review looking tested.
/// </summary>
internal sealed class RequiresSymbolicLinkSupportFactAttribute : FactAttribute
{
    private static readonly Lazy<string?> LazySkipReason = new(Probe);

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="RequiresSymbolicLinkSupportFactAttribute"/> class.
    /// </summary>
    public RequiresSymbolicLinkSupportFactAttribute()
    {
        Skip = LazySkipReason.Value;
    }

    /// <summary>
    /// Creates a link rather than inferring the privilege from the platform, so that a Windows
    /// machine which does grant it still runs the test instead of being excluded by assumption.
    /// Any failure counts as the privilege being absent, including one raised while preparing the
    /// probe, because this runs during discovery: an exception leaving here removes the whole test
    /// class from the run and reports a pass, which is the outcome the attribute exists to prevent.
    /// </summary>
    /// <returns>The reason to skip, or <see langword="null"/> when links can be created.</returns>
    private static string? Probe()
    {
        DirectoryInfo? probeRoot = null;
        try
        {
            probeRoot = Directory.CreateTempSubdirectory("workerproxy-symlink-probe");
            string target = Path.Combine(probeRoot.FullName, "target");
            File.WriteAllBytes(target, []);
            File.CreateSymbolicLink(Path.Combine(probeRoot.FullName, "link"), target);

            return null;
        }
        catch (Exception exception)
        {
            return $"Requires symbolic link creation, which this environment withholds ({exception.GetType().Name}).";
        }
        finally
        {
            TryDelete(probeRoot);
        }
    }

    /// <summary>
    /// Removes the probe directory, tolerating failure so that cleanup cannot be what drops the
    /// test class from discovery.
    /// </summary>
    /// <param name="probeRoot">The directory to remove, or <see langword="null"/> when none was created.</param>
    private static void TryDelete(DirectoryInfo? probeRoot)
    {
        try
        {
            probeRoot?.Delete(recursive: true);
        }
        catch (Exception)
        {
            // A directory left in the temp path is not worth losing the tests over.
        }
    }
}
