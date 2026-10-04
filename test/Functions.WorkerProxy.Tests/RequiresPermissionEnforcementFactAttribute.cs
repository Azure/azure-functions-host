// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

/// <summary>
/// Marks a test that asserts against denied filesystem access, and reports it as skipped where
/// a denial does not take hold. Root, and any process holding CAP_DAC_OVERRIDE, reads a path
/// whose permissions forbid it, so such a test proves nothing there; returning early instead
/// would report that as a pass.
/// </summary>
internal sealed class RequiresPermissionEnforcementFactAttribute : FactAttribute
{
    private static readonly Lazy<string?> LazySkipReason = new(Probe);

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="RequiresPermissionEnforcementFactAttribute"/> class.
    /// </summary>
    public RequiresPermissionEnforcementFactAttribute()
    {
        Skip = LazySkipReason.Value;
    }

    /// <summary>
    /// Denies access to a probe directory and confirms the denial is observed, because whether
    /// permissions bind depends on the account a run uses rather than on the platform. Any failure
    /// to set the probe up counts as unavailable, because this runs during discovery: an exception
    /// leaving here removes the whole test class from the run and reports a pass.
    /// </summary>
    /// <returns>
    /// The reason to skip, or <see langword="null"/> when denied access is enforced.
    /// </returns>
    private static string? Probe()
    {
        if (OperatingSystem.IsWindows())
        {
            return "Requires Unix permission semantics.";
        }

        DirectoryInfo? probeRoot = null;
        string? deniedDirectory = null;
        try
        {
            probeRoot = Directory.CreateTempSubdirectory("workerproxy-permission-probe");
            deniedDirectory = Path.Combine(probeRoot.FullName, "denied");
            Directory.CreateDirectory(deniedDirectory);
            string deniedFile = Path.Combine(deniedDirectory, "denied-file");
            File.WriteAllBytes(deniedFile, []);
            File.SetUnixFileMode(deniedDirectory, UnixFileMode.None);

            return ReadsDeniedPath(deniedFile)
                ? "Requires permission enforcement; this process reads paths that deny it."
                : null;
        }
        catch (Exception exception)
        {
            return $"Requires permission enforcement, which could not be probed here ({exception.GetType().Name}).";
        }
        finally
        {
            TryRestore(deniedDirectory);
            TryDelete(probeRoot);
        }
    }

    /// <summary>
    /// Reports whether the denied path is still readable, which is what a privileged account does.
    /// The denial is read only here, so that a failure to prepare the probe is never mistaken for
    /// permissions being enforced.
    /// </summary>
    /// <param name="deniedFile">A path inside a directory whose permissions forbid access.</param>
    /// <returns><see langword="true"/> when the path is read in spite of the denial.</returns>
    private static bool ReadsDeniedPath(string deniedFile)
    {
        try
        {
            _ = File.GetAttributes(deniedFile);

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Restores access to the probe directory so it can be removed, tolerating failure so that
    /// cleanup cannot be what drops the test class from discovery.
    /// </summary>
    /// <param name="deniedDirectory">The directory to restore, or <see langword="null"/> when none was created.</param>
    private static void TryRestore(string? deniedDirectory)
    {
        if (deniedDirectory is null || OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(deniedDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception)
        {
            // Only worth attempting; the directory below is removed on a best-effort basis too.
        }
    }

    /// <summary>
    /// Removes the probe directory, tolerating failure for the same reason as <see cref="TryRestore"/>.
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
