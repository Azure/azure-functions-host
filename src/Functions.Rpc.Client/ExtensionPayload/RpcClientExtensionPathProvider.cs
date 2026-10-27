// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;

namespace Azure.Functions.Rpc.Client;

internal sealed class RpcClientExtensionPathProvider(WorkerExtensionPayloadManager payloadManager) : DefaultExtensionPathProvider
{
    private readonly WorkerExtensionPayloadManager _payloadManager = payloadManager ?? throw new ArgumentNullException(nameof(payloadManager));

    public override bool UseExtensionBundles => _payloadManager.DirectoryPath is null;

    public override bool TryResolveExtensionsMetadataPath(
        string rootScriptPath, out string metadataPath, out string baseProbingPath)
    {
        string directory = _payloadManager.DirectoryPath;
        if (directory is null)
        {
            return base.TryResolveExtensionsMetadataPath(rootScriptPath, out metadataPath, out baseProbingPath);
        }

        metadataPath = directory;
        baseProbingPath = Path.Combine(directory, ScriptConstants.AzureFunctionsSystemDirectoryName);
        return true;
    }
}
