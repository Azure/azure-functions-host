// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions
{
    public static class DescriptionLoggerExtension
    {
        // EventId range is 1-99

        private static readonly Action<ILogger, string, string, Exception> _assemblyDynamiclyResolved =
        LoggerMessage.Define<string, string>(LogLevel.Trace, new EventId(1, nameof(AssemblyResolved)),
            "Resolved assembly '{assemblyName}' in the function load context using the dynamic resolver for function '{functionName}'");

        private static readonly Action<ILogger, string, string, Exception> _assemblyDynamiclyResolutionFailure =
            LoggerMessage.Define<string, string>(LogLevel.Trace, new EventId(2, nameof(AssemblyResolutionFailure)),
                "Attempt to resolve assembly '{assemblyName}' failed in the function load context using the dynamic resolver for function '{functionName}'");

        private static readonly Action<ILogger, string, string, Exception> _assemblyResolved =
        LoggerMessage.Define<string, string>(LogLevel.Trace, new EventId(3, nameof(AssemblyResolved)),
            "Resolved assembly '{assemblyName}' in the function load context using the resolver '{assemblyResolverName}'");

        private static readonly Action<ILogger, string, string, Exception> _assemblyResolutionFailure =
            LoggerMessage.Define<string, string>(LogLevel.Trace, new EventId(4, nameof(AssemblyResolutionFailure)),
                "Attempt to resolve assembly '{assemblyName}' failed in the function load context using the resolver '{assemblyResolverName}'");

        public static void AssemblyDynamiclyResolved(this ILogger logger, string assemblyName, string functionName)
        {
            _assemblyDynamiclyResolved(logger, assemblyName, functionName, null);
        }

        public static void AssemblyDynamiclyResolutionFailure(this ILogger logger, string assemblyName, string functionName)
        {
            _assemblyDynamiclyResolutionFailure(logger, assemblyName, functionName, null);
        }

        public static void AssemblyResolved(this ILogger logger, string assemblyName, string assemblyResolverName)
        {
            _assemblyResolved(logger, assemblyName, assemblyResolverName, null);
        }

        public static void AssemblyResolutionFailure(this ILogger logger, string assemblyName, string assemblyResolverName)
        {
            _assemblyResolutionFailure(logger, assemblyName, assemblyResolverName, null);
        }
    }
}
