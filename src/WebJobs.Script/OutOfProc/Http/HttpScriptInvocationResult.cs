// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.OutOfProc.Http
{
    public class HttpScriptInvocationResult
    {
        public object ReturnValue { get; set; }

        public IDictionary<string, object> Outputs { get; set; }

        public List<string> Logs { get; set; }
    }
}
