// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    internal class FunctionsSdkVersionProvider : ISdkVersionProvider
    {
        public string GetSdkVersion()
        {
            return $"azurefunctions: {ScriptHost.Version}";
        }
    }
}
