// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Microsoft.Azure.WebJobs.Script.PublicApi.Tests;

/// <summary>
/// Resolves repository and build-output paths independently of the current working directory.
/// </summary>
internal static class RepositoryPaths
{
    private const string RepositoryMarkerFile = "WebJobs.Script.sln";

    /// <summary>
    /// Gets the absolute path of the repository root.
    /// </summary>
    /// <returns>The absolute repository root path.</returns>
    public static string FindRepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        return TryFindRepositoryRoot(Path.GetDirectoryName(sourceFilePath))
            ?? TryFindRepositoryRoot(AppContext.BaseDirectory)
            ?? TryFindRepositoryRoot(Directory.GetCurrentDirectory())
            ?? throw new DirectoryNotFoundException($"Unable to locate '{RepositoryMarkerFile}'.");
    }

    /// <summary>
    /// Combines repository-relative segments into an absolute path.
    /// </summary>
    /// <param name="relativePath">The repository-relative path, using forward or backward slashes.</param>
    /// <returns>The absolute path.</returns>
    public static string Combine(string relativePath)
    {
        return Path.GetFullPath(Path.Combine(FindRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string TryFindRepositoryRoot(string startPath)
    {
        if (string.IsNullOrEmpty(startPath))
        {
            return null;
        }

        var directory = new DirectoryInfo(startPath);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, RepositoryMarkerFile)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
