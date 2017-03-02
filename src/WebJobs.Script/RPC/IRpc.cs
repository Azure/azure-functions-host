// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public interface IRpc
    {
        string GetRpcProvider();

        Task<LanguageInvokerInitializationResult> SetupNodeRpcWorker(TraceWriter systemTraceWriter);

        // void ClearRequiredCache();

        Task<LanguageInvokerInitializationResult> SetupDotNetRpcWorker(TraceWriter systemTraceWriter);

        Task<object> SendMessageToRpcWorker(ScriptType scriptType, string scriptFilePath, object[] parameters, FunctionInvocationContext context, Dictionary<string, object> scriptExecutionContext);
    }
}
