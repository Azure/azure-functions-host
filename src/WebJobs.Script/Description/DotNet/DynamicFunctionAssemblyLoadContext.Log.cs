// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal partial class DynamicFunctionAssemblyLoadContext
    {
        private static class Logger
        {
            private static readonly Action<ILogger, string, string, Exception> _assemblyResolved =
           LoggerMessage.Define<string, string>(LogLevel.Trace, new EventId(1, nameof(AssemblyResolved)),
               "Resolved assembly '{assemblyName}' in the function load context using the dynamic resolver for function '{functionName}'");

            private static readonly Action<ILogger, string, string, Exception> _assemblyResolutionFailure =
               LoggerMessage.Define<string, string>(LogLevel.Trace, new EventId(2, nameof(AssemblyResolutionFailure)),
                   "Attempt to resolve assembly '{assemblyName}' failed in the function load context using the dynamic resolver for function '{functionName}'");

            public static void AssemblyResolved(ILogger logger, string assemblyName, string functionName)
            {
                _assemblyResolved(logger, assemblyName, functionName, null);
            }

            public static void AssemblyResolutionFailure(ILogger logger, string assemblyName, string functionName)
            {
                _assemblyResolutionFailure(logger, assemblyName, functionName, null);
            }
        }
    }
}
