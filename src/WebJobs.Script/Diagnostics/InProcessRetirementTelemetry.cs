// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics;

internal static partial class InProcessRetirementTelemetry
{
    // The proposed file parser lets custom handlers claim their script files regardless of
    // extension. Don't change the language used by today's host to model that exception.
    private static readonly ConditionalWeakTable<FunctionMetadata, object> CustomHandlerFiles = new();

    internal static void TrackCustomHandlerFile(FunctionMetadata metadata) => CustomHandlerFiles.GetOrCreateValue(metadata);

    internal static void LogMetadata(ILogger logger, IEnumerable<FunctionMetadata> functions, bool fromExtension)
    {
        if (!logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        int direct = 0;
        int script = 0;
        int assembly = 0;
        int rawAssembly = 0;
        foreach (FunctionMetadata function in functions)
        {
            if (function.IsProxy() || !function.IsDotNetInProc())
            {
                continue;
            }

            bool isAssembly = string.Equals(function.Language, DotNetScriptTypes.DotNetAssembly, StringComparison.OrdinalIgnoreCase);
            if (fromExtension && function.IsCodeless() && isAssembly)
            {
                continue;
            }

            if (function.IsDirect())
            {
                direct++;
            }
            else if (CustomHandlerFiles.TryGetValue(function, out _))
            {
                continue;
            }
            else if (isAssembly)
            {
                assembly++;
            }
            else if (string.Equals(function.Language, DotNetScriptTypes.CSharp, StringComparison.OrdinalIgnoreCase))
            {
                script++;
            }
            else
            {
                rawAssembly++;
            }
        }

        if (direct + script + assembly + rawAssembly > 0)
        {
            InProcessRetirementMetadata(logger, fromExtension ? "Extension" : "App", direct, script, assembly, rawAssembly);
        }
    }

    [LoggerMessage(421, LogLevel.Debug, "In-process retirement would reject the selected dotnet runtime.")]
    internal static partial void InProcessRetirementRuntime(ILogger logger);

    [LoggerMessage(422, LogLevel.Debug, "In-process retirement would reject {source} function metadata: Direct={direct}, CSharp={script}, DotNetAssembly={assembly}, RawDotNetAssembly={rawAssembly}.")]
    private static partial void InProcessRetirementMetadata(ILogger logger, string source, int direct, int script, int assembly, int rawAssembly);

    [LoggerMessage(423, LogLevel.Debug, "The dotnet-isolated payload mismatch compatibility path was used.")]
    internal static partial void InProcessRetirementIsolatedFallback(ILogger logger);
}
