// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Azure.Functions.WorkerProxy.Artifacts;

internal sealed partial class ExtensionArtifactService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Extension artifact serving is disabled. Reason: {Reason}")]
    private static partial void LogArtifactServingDisabled(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Extension artifact archive prepared. Path='{ArchivePath}', Digest='{Digest}', Size={SizeBytes} bytes.")]
    private static partial void LogArchivePrepared(ILogger logger, string archivePath, string digest, long sizeBytes);
}
