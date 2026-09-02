// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Azure.Functions.WorkerProxy.ExtensionArtifacts;

internal sealed partial class ExtensionArtifactShim
{
    // Reported when the inputs an artifact needs are not usable: absent, of the wrong kind, or
    // an extensions directory holding no files. The worker SDK writes extensions.json and
    // .azurefunctions into every publish output, so an input that is missing or malformed means
    // the deployment package was assembled incorrectly rather than that there is nothing to
    // shim. That is the customer's to fix, so it is reported at the level the host already uses
    // for its equivalent .azurefunctions checks and stays visible where Debug is off.
    [LoggerMessage(1, LogLevel.Warning, "Extension artifacts are unavailable. Reason: {Reason}")]
    private static partial void LogArtifactsUnavailable(ILogger logger, string reason);

    // Kept at Information: it is emitted once per artifact creation, and the entry count and
    // size are what correlate a running worker with a deployment when Debug is off in
    // production.
    [LoggerMessage(2, LogLevel.Information, "Extension artifact archive prepared. Entries={EntryCount}, Size={SizeBytes} bytes.")]
    private static partial void LogArchivePrepared(ILogger logger, int entryCount, long sizeBytes);
}
