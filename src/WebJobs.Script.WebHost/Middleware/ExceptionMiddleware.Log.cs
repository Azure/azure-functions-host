// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    internal partial class ExceptionMiddleware
    {
        private static class Logger
        {
            private static readonly Action<ILogger, Exception> _responseStarted =
               LoggerMessage.Define(LogLevel.Debug, new EventId(1, nameof(ResponseStarted)), "The response has already started, the status code will not be modified.");

            private static readonly Action<ILogger, Exception> _unhandledHostError =
                LoggerMessage.Define(LogLevel.Error, new EventId(2, nameof(UnhandledHostError)), "An unhandled host error has occurred.");

            public static void ResponseStarted(ILogger logger) => _responseStarted(logger, null);

            public static void UnhandledHostError(ILogger logger, Exception ex) => _unhandledHostError(logger, ex);
        }
    }
}
