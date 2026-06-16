// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc;

internal static class RpcWorkerChannelExtensions
{
    /// <summary>
    /// Shuts down the channel, failing any pending executions, and then disposes it.
    /// Exceptions thrown during shutdown are logged but do not propagate.
    /// </summary>
    /// <param name="channel">The worker channel to shut down and dispose.</param>
    /// <param name="exception">The exception that caused the shutdown, or <c>null</c> for a graceful shutdown.</param>
    /// <param name="logger">The logger to use for error reporting.</param>
    /// <example>
    /// <code>
    /// channel.ShutdownAndDispose(workerException, _logger);
    /// </code>
    /// </example>
    public static void ShutdownAndDispose(this IRpcWorkerChannel channel, Exception exception, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            channel.Shutdown(exception);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error failing executions on shutdown for language worker channel with id:{workerId}", channel.Id);
        }

        (channel as IDisposable)?.Dispose();
    }
}
