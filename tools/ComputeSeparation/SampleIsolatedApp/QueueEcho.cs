// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Functions.Worker;

namespace SampleIsolatedApp;

/// <summary>
/// Returns a queue-trigger input through the Host's queue output binding.
/// </summary>
public static class QueueEcho
{
    /// <summary>
    /// Echoes a message delivered by the Host's queue listener.
    /// </summary>
    /// <param name="message">The input-queue message.</param>
    /// <returns>The output-queue message written by the Host after invocation completes.</returns>
    [Function(nameof(QueueEcho))]
    [QueueOutput("%ExtensionPayloadOutputQueue%")]
    public static string Run([QueueTrigger("%ExtensionPayloadInputQueue%")] string message)
    {
        return message;
    }
}
