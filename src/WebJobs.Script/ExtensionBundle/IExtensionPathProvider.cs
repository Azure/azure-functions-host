// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.ExtensionBundle;

/// <summary>
/// Resolves extension metadata and assembly probing paths for the current application.
/// </summary>
public interface IExtensionPathProvider
{
    /// <summary>
    /// Gets a value indicating whether configured extension bundles may supply extensions.
    /// </summary>
    bool UseExtensionBundles { get; }

    /// <summary>
    /// Resolves the application extension metadata directory and its assembly probing directory.
    /// </summary>
    /// <param name="rootScriptPath">The application script directory.</param>
    /// <param name="metadataPath">The directory containing <c>extensions.json</c>, when found.</param>
    /// <param name="baseProbingPath">The assembly probing directory, or <see langword="null"/> to use the metadata directory.</param>
    /// <returns>Whether an extension metadata location was resolved.</returns>
    bool TryResolveExtensionsMetadataPath(string rootScriptPath, out string metadataPath, out string baseProbingPath);
}
