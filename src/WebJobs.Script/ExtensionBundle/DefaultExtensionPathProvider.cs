// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.ExtensionBundle;

/// <summary>
/// Uses the existing application-local extension layouts and configured extension bundles.
/// </summary>
public class DefaultExtensionPathProvider : IExtensionPathProvider
{
    /// <inheritdoc/>
    public virtual bool UseExtensionBundles => true;

    /// <inheritdoc/>
    public virtual bool TryResolveExtensionsMetadataPath(
        string rootScriptPath, out string metadataPath, out string baseProbingPath)
    {
        ArgumentNullException.ThrowIfNull(rootScriptPath);
        return Utility.TryResolveExtensionsMetadataPath(rootScriptPath, out metadataPath, out baseProbingPath);
    }
}
