// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    public static class HttpWorkerConstants
    {
        // Headers
        public const string InvocationIdHeaderName = "X-Azure-Functions-InvocationId";
        public const string HostVersionHeaderName = "X-Azure-Functions-HostVersion";
        public const string UserAgentHeaderValue = "Azure-Functions-Host";

        // Child Process Env vars
        public const string PortEnvVarName = "FUNCTIONS_HTTPWORKER_PORT";
        public const string WorkerIdEnvVarName = "FUNCTIONS_HTTPWORKER_ID";
        public const string FunctionAppRootVarName = "FUNCTIONS_APP_ROOT_PATH";
        public const string CustomHandlerPortEnvVarName = "FUNCTIONS_CUSTOMHANDLER_PORT";
        public const string CustomHandlerWorkerIdEnvVarName = "FUNCTIONS_CUSTOMHANDLER_WORKER_ID";
    }
}
