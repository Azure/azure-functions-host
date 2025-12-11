// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.Extensions
{
    internal static partial class WebHostWorkerRuntimeResolverLoggerExtensions
    {
        [LoggerMessage(
            EventId = 700,
            Level = LogLevel.Debug,
            Message = "Script host worker resolver resolved successfully.")]
        public static partial void ScriptHostWorkerResolverResolvedSuccessfully(this ILogger logger);

        [LoggerMessage(
            EventId = 701,
            Level = LogLevel.Debug,
            Message = "Script host worker resolver cached.")]
        public static partial void ScriptHostWorkerResolverCached(this ILogger logger);

        [LoggerMessage(
            EventId = 702,
            Level = LogLevel.Debug,
            Message = "Subscribed to ScriptHostManager ActiveHostChanged event.")]
        public static partial void SubscribedToActiveHostChangedEvent(this ILogger logger);

        [LoggerMessage(
            EventId = 703,
            Level = LogLevel.Debug,
            Message = "Active host changed. Cached worker runtime resolver cleared.")]
        public static partial void ActiveHostChangedResolverCleared(this ILogger logger);
    }
}
