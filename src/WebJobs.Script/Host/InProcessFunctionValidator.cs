// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script;

internal static class InProcessFunctionValidator
{
    internal const string ErrorMessage = ".NET in-process functions are no longer supported. Migrate the application to the .NET isolated worker model. See https://aka.ms/azure-functions-retirements/in-process-model for more information.";

    internal static void ValidateRuntime(string runtime, bool isPlaceholder, bool isMultiLanguage = false)
    {
        // Multi-language hosts select execution per function, not from FUNCTIONS_WORKER_RUNTIME.
        // ValidateMetadata still rejects customer in-process payloads and extension scripts.
        if (!isPlaceholder && !isMultiLanguage
            && string.Equals(runtime, RpcWorkerConstants.DotNetLanguageWorkerName, StringComparison.OrdinalIgnoreCase))
        {
            throw new HostInitializationException(ErrorMessage);
        }
    }

    internal static void ValidateMetadata(FunctionMetadata metadata)
    {
        if (metadata.IsDotNetInProc()
            && !(metadata.IsExtensionFunction() && metadata.IsCodeless()
                && string.Equals(metadata.Language, DotNetScriptTypes.DotNetAssembly, StringComparison.OrdinalIgnoreCase)))
        {
            throw new HostInitializationException($"Function '{metadata.Name}': {ErrorMessage}");
        }
    }
}
